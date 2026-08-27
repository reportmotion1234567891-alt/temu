Imports System.Configuration
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

Public Class TemuService

    Private Shared ReadOnly AppKey As String = ConfigurationManager.AppSettings("TemuAppKey")
    Private Shared ReadOnly AppSecret As String = ConfigurationManager.AppSettings("TemuAppSecret")
    Private Shared ReadOnly ApiUrl As String = ConfigurationManager.AppSettings("NewApiUrl")

    Private Shared ReadOnly Http As New HttpClient()

    Private Shared ReadOnly TemplateCache As New Dictionary(Of Long, JObject)
    Private Shared ReadOnly TaxCodeCache As New Dictionary(Of Long, String)
    Private Shared ReadOnly ComplianceCache As New Dictionary(Of Integer, JArray)
    Private Shared TrademarkCache As JArray = Nothing
    Private Shared ExistingSkuMap As Dictionary(Of String, Long) = Nothing

    Private Const RefPidSeason As Long = 76
    Private Const RefPidBrand As Long = 1960
    Private Const RefPidTireDiameter As Long = 7679
    Private Const RefPidTireWidth As Long = 7680
    Private Const RefPidWheelDiameter As Long = 7681
    Private Const RefPidTireSize As Long = 1246
    Private Const RefPidLoadIndex As Long = 8551
    Private Const RefPidSpeedRating As Long = 7398
    Private Const RefPidAspectRatio As Long = 8552

    Private Const UnitInch As Long = 15
    Private Const UnitIndex As Long = 254
    Private Const UnitPercent As Long = 57
    Public Shared ForceRecreate As Boolean = False
    Private Shared ReadOnly SpeedRatingVids As New Dictionary(Of String, Long)(StringComparer.OrdinalIgnoreCase) From {
        {"L", 543124}, {"M", 543125}, {"N", 543126}, {"P", 543127},
        {"Q", 543128}, {"R", 543129}, {"S", 543130}, {"T", 543131},
        {"U", 543132}, {"H", 543133}, {"V", 543134}, {"W", 543135},
        {"Y", 543136}
    }
    Private Shared ReadOnly V2Endpoints As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "temu.local.goods.v2.add",
        "temu.local.goods.image.v2.upload",
        "temu.local.product.attributes.get",
        "temu.local.product.variation.get"
    }

    Private Shared Function VersionFor(apiType As String) As String
        If V2Endpoints.Contains(apiType) Then Return "V2"
        Return "V1"
    End Function
    Private Shared Function GetMD5(input As String) As String
        Using hasher As MD5 = MD5.Create()
            Dim hash = hasher.ComputeHash(Encoding.UTF8.GetBytes(input))
            Dim sb As New StringBuilder()
            For Each b In hash
                sb.Append(b.ToString("x2"))
            Next
            Return sb.ToString().ToUpper()
        End Using
    End Function

    Private Shared Function TokenToSignValue(token As JToken) As String
        If token Is Nothing OrElse token.Type = JTokenType.Null Then Return ""
        Select Case token.Type
            Case JTokenType.Object, JTokenType.Array
                Return token.ToString(Formatting.None)
            Case JTokenType.Boolean
                Return If(token.Value(Of Boolean)(), "true", "false")
            Case Else
                Return token.ToString()
        End Select
    End Function

    Private Shared Async Function SendRequestWrapped(apiType As String, requestObj As Object) As Task(Of JObject)
        Dim accessToken = TokenStorage.AccessToken
        Dim timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
        Dim requestJson As String = If(requestObj Is Nothing, "{}", JsonConvert.SerializeObject(requestObj))

        Dim parameters As New SortedDictionary(Of String, String)
        parameters("access_token") = accessToken
        parameters("app_key") = AppKey
        parameters("data_type") = "JSON"
        parameters("request") = requestJson
        parameters("timestamp") = timestamp
        parameters("type") = apiType
        parameters("version") = "V1"

        Dim sb As New StringBuilder()
        sb.Append(AppSecret)
        For Each kv In parameters
            sb.Append(kv.Key).Append(kv.Value)
        Next
        sb.Append(AppSecret)
        parameters("sign") = GetMD5(sb.ToString())

        Return Await PostJson(parameters)
    End Function

    Private Shared Async Function SendRequestFlat(apiType As String, businessObj As Object) As Task(Of JObject)
        Dim accessToken = TokenStorage.AccessToken
        Dim timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
        Dim apiVersion As String = If(apiType.StartsWith("temu.local."), "V2", "V1")

        Dim body As New JObject()
        body("access_token") = accessToken
        body("app_key") = AppKey
        body("data_type") = "JSON"
        body("timestamp") = timestamp
        body("type") = apiType
        body("version") = VersionFor(apiType)

        Dim businessJson As JObject
        If TypeOf businessObj Is JObject Then
            businessJson = CType(businessObj, JObject)
        Else
            businessJson = JObject.FromObject(businessObj,
                JsonSerializer.Create(New JsonSerializerSettings With {
                    .NullValueHandling = NullValueHandling.Ignore
                }))
        End If
        For Each prop In businessJson.Properties()
            body(prop.Name) = prop.Value
        Next

        Dim signMap As New SortedDictionary(Of String, String)
        For Each prop In body.Properties()
            If prop.Name = "sign" Then Continue For
            If prop.Value Is Nothing OrElse prop.Value.Type = JTokenType.Null Then Continue For
            signMap(prop.Name) = TokenToSignValue(prop.Value)
        Next

        Dim sb As New StringBuilder()
        sb.Append(AppSecret)
        For Each kv In signMap
            sb.Append(kv.Key).Append(kv.Value)
        Next
        sb.Append(AppSecret)
        body("sign") = GetMD5(sb.ToString())

        Return Await PostJson(body)
    End Function

    Private Shared Async Function PostJson(body As Object) As Task(Of JObject)
        Dim finalJson = JsonConvert.SerializeObject(body)
        Dim content = New StringContent(finalJson, Encoding.UTF8, "application/json")

        Console.WriteLine("REQUEST: " & finalJson)
        Dim response = Await Http.PostAsync(ApiUrl, content)
        Dim result = Await response.Content.ReadAsStringAsync()
        Console.WriteLine("RESPONSE: " & result)
        Return JObject.Parse(result)
    End Function

    Public Shared Function GetCategory() As Long
        Return CLng(ConfigurationManager.AppSettings("TemuTireCategoryId"))
    End Function

    Public Shared Function GetShippingTemplate() As String
        Return ConfigurationManager.AppSettings("TemuShippingTemplateId")
    End Function

    Public Shared Function GetOriginRegion1() As String
        Return ConfigurationManager.AppSettings("TemuOriginRegion1")
    End Function

    Public Shared Function GetOriginRegion2() As String
        Return ConfigurationManager.AppSettings("TemuOriginRegion2")
    End Function

    Public Shared Async Function UploadImage(imageUrl As String, catId As Long) As Task(Of String)
        Dim req = New With {
            .fileUrl = imageUrl,
            .catId = catId,
            .usage = 3
        }

        Dim res = Await SendRequestFlat("temu.local.goods.image.v2.upload", req)

        If Not res("success").Value(Of Boolean)() Then
            Throw New Exception("Image upload failed: " & res.ToString())
        End If

        Dim result = res("result")
        If result Is Nothing Then Throw New Exception("Image upload returned no result: " & res.ToString())

        Dim images = TryCast(result("images"), JArray)
        If images IsNot Nothing AndAlso images.Count > 0 Then
            Dim firstImg = images(0)
            If firstImg("url") IsNot Nothing Then Return firstImg("url").ToString()
            If firstImg("imageUrl") IsNot Nothing Then Return firstImg("imageUrl").ToString()
        End If
        If result("url") IsNot Nothing Then Return result("url").ToString()
        If result("imageUrl") IsNot Nothing Then Return result("imageUrl").ToString()
        If result("fileUrl") IsNot Nothing Then Return result("fileUrl").ToString()

        Throw New Exception("Could not find image URL in response: " & res.ToString())
    End Function

    Public Shared Async Function GetTemplate(catId As Long) As Task(Of JObject)
        If TemplateCache.ContainsKey(catId) Then Return TemplateCache(catId)

        Dim req = New With {.catId = catId}
        Dim res = Await SendRequestFlat("bg.local.goods.template.get", req)

        If Not res("success").Value(Of Boolean)() Then
            Throw New Exception("template.get failed: " & res.ToString())
        End If

        TemplateCache(catId) = res
        Return res
    End Function

    Public Shared Async Function DumpTemplateProperties(catId As Long) As Task
        Console.WriteLine("=== DumpTemplateProperties catId=" & catId & " ===")

        Dim res = Await GetTemplate(catId)
        Dim resultObj = TryCast(res("result"), JObject)
        If resultObj Is Nothing Then
            Console.WriteLine(res.ToString(Formatting.Indented))
            Return
        End If

        Dim templateInfo = TryCast(resultObj("templateInfo"), JObject)
        If templateInfo Is Nothing Then
            Console.WriteLine(resultObj.ToString(Formatting.Indented))
            Return
        End If

        Dim arr = TryCast(templateInfo("goodsProperties"), JArray)
        If arr Is Nothing Then
            Console.WriteLine(templateInfo.ToString(Formatting.Indented))
            Return
        End If

        Console.WriteLine("--- goodsProperties (" & arr.Count & " entries) ---")
        For Each prop In arr
            Dim refPid = If(prop("refPid") IsNot Nothing, prop("refPid").ToString(), "-")
            Dim name = If(prop("name") IsNot Nothing, prop("name").ToString(), "-")
            Dim required = If(prop("required") IsNot Nothing, prop("required").ToString(), "-")
            Dim controlType = If(prop("controlType") IsNot Nothing, prop("controlType").ToString(), "-")

            Console.WriteLine("")
            Console.WriteLine($"refPid={refPid}  name='{name}'  required={required}  controlType={controlType}")

            Dim values = TryCast(prop("values"), JArray)
            If values IsNot Nothing Then
                For Each v In values
                    Console.WriteLine($"      vid={v("vid")}  value='{v("value")}'")
                Next
            End If
        Next

        Console.WriteLine("=== DumpTemplateProperties DONE ===")
    End Function

    Public Shared Async Function GenerateSpecId(catId As Long, parentSpecId As Long, childSpecName As String) As Task(Of Long)
        Dim req = New With {
            .catId = catId,
            .parentSpecId = parentSpecId,
            .childSpecName = childSpecName
        }
        Dim res = Await SendRequestFlat("bg.local.goods.spec.id.get", req)

        If Not res("success").Value(Of Boolean)() Then
            Throw New Exception("spec.id.get failed: " & res.ToString())
        End If

        Dim specIdToken = res("result")("specId")
        If specIdToken Is Nothing Then Throw New Exception("spec.id.get returned no specId: " & res.ToString())

        Return specIdToken.Value(Of Long)()
    End Function

    Public Shared Async Function ResolveSpec(catId As Long, childSpecName As String) As Task(Of (parentSpecId As Long, specId As Long, specName As String))
        Dim template = Await GetTemplate(catId)
        Dim result = template("result")

        Dim userInputList = TryCast(result("userInputParentSpecList"), JArray)
        If userInputList IsNot Nothing AndAlso userInputList.Count > 0 Then
            Dim parent = userInputList(0)
            Dim parentSpecId = parent("parentSpecId").Value(Of Long)()
            Dim specId = Await GenerateSpecId(catId, parentSpecId, childSpecName)
            Return (parentSpecId, specId, childSpecName)
        End If

        Dim specProps = TryCast(result("templateInfo")("goodsSpecProperties"), JArray)
        If specProps IsNot Nothing Then
            For Each spec In specProps
                Dim values = TryCast(spec("values"), JArray)
                If values Is Nothing OrElse values.Count = 0 Then Continue For

                Dim firstValue = values(0)
                Dim parentSpecId = If(spec("parentSpecId") IsNot Nothing,
                                      spec("parentSpecId").Value(Of Long)(),
                                      0L)
                Dim specId = firstValue("specId").Value(Of Long)()
                Dim specName = firstValue("value").ToString()
                Return (parentSpecId, specId, specName)
            Next
        End If

        Throw New Exception("No usable parentSpecId found for catId " & catId)
    End Function

    Public Shared Async Function GetTaxCode(catId As Long) As Task(Of String)
        If TaxCodeCache.ContainsKey(catId) Then Return TaxCodeCache(catId)

        Dim req = New With {.catId = catId}
        Dim res = Await SendRequestFlat("bg.local.goods.tax.code.get", req)

        If Not res("success").Value(Of Boolean)() Then
            Dim errCode = res("errorCode").Value(Of Integer)()
            If errCode = 150011026 Then
                TaxCodeCache(catId) = ""
                Return ""
            End If
            Throw New Exception("tax.code.get failed: " & res.ToString())
        End If

        Dim list = TryCast(res("result")("itemTaxCodeList"), JArray)
        If list Is Nothing OrElse list.Count = 0 Then
            TaxCodeCache(catId) = ""
            Return ""
        End If

        For Each item In list
            If item("isSuggest") IsNot Nothing AndAlso item("isSuggest").Value(Of Boolean)() Then
                Dim code = item("itemTaxCode").ToString()
                TaxCodeCache(catId) = code
                Return code
            End If
        Next

        Dim firstCode = list(0)("itemTaxCode").ToString()
        TaxCodeCache(catId) = firstCode
        Return firstCode
    End Function

    Public Shared Async Function GetTrademarks() As Task(Of JArray)
        If TrademarkCache IsNot Nothing Then Return TrademarkCache

        Dim all As New JArray()
        Dim page As Integer = 1

        Do
            Dim req = New With {.page = page, .size = 100}
            Dim res = Await SendRequestFlat("temu.local.goods.brand.trademark.V2.get", req)

            If Not res("success").Value(Of Boolean)() Then
                Throw New Exception("trademark.V2.get failed: " & res.ToString())
            End If

            Dim list = TryCast(res("result")("trademarkList"), JArray)
            If list Is Nothing OrElse list.Count = 0 Then Exit Do

            For Each tm In list
                all.Add(tm)
            Next

            Dim totalToken = res("result")("totalNum")
            Dim total As Long = If(totalToken IsNot Nothing, totalToken.Value(Of Long)(), 0L)
            If all.Count >= total Then Exit Do
            page += 1
        Loop While page < 50

        TrademarkCache = all
        Return all
    End Function
    Public Shared Async Function DebugListGoods() As Task
        Console.WriteLine("=== DebugListGoods ===")

        Dim req As New JObject()
        req("page") = 1
        req("pageSize") = 20

        Dim res = Await SendRequestFlat("bg.local.goods.list.query", req)
        Console.WriteLine(res.ToString(Formatting.Indented))
        Console.WriteLine("=== DONE ===")
    End Function
    Public Shared Async Function DebugComplianceDetail(goodsId As Long) As Task
        Console.WriteLine("=== DebugComplianceDetail goodsId=" & goodsId & " ===")
        Dim req As New JObject()
        req("goodsId") = goodsId
        Dim res = Await SendRequestFlat("bg.local.goods.detail.query", req)
        Console.WriteLine(res.ToString(Formatting.Indented))
        Console.WriteLine("=== DONE ===")
    End Function
    Public Shared Async Function DebugMallInfo() As Task
        Console.WriteLine("=== DebugMallInfo ===")
        Dim res = Await SendRequestFlat("bg.open.accesstoken.info", New JObject())
        Console.WriteLine(res.ToString(Formatting.Indented))
        Console.WriteLine("=== DONE ===")
    End Function
    Public Shared Async Function ResolveTrademark(brandName As String) As Task(Of (trademarkId As Long?, brandId As Long?))
        If String.IsNullOrWhiteSpace(brandName) Then Return (Nothing, Nothing)

        Dim trademarks = Await GetTrademarks()
        For Each tm In trademarks
            Dim bn = If(tm("brandName") IsNot Nothing, tm("brandName").ToString(), "")
            Dim tn = If(tm("trademarkName") IsNot Nothing, tm("trademarkName").ToString(), "")
            If String.Equals(bn, brandName, StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(tn, brandName, StringComparison.OrdinalIgnoreCase) Then
                Dim tmId As Long? = If(tm("trademarkId") IsNot Nothing, CType(tm("trademarkId").Value(Of Long)(), Long?), Nothing)
                Dim bId As Long? = If(tm("brandId") IsNot Nothing, CType(tm("brandId").Value(Of Long)(), Long?), Nothing)
                Return (tmId, bId)
            End If
        Next
        Return (Nothing, Nothing)
    End Function

    Public Shared Async Function GetCompliancePersons(complianceInfoType As Integer) As Task(Of JArray)
        If ComplianceCache.ContainsKey(complianceInfoType) Then Return ComplianceCache(complianceInfoType)

        Dim all As New JArray()
        Dim page As Integer = 1

        Do
            Dim req = New With {
                .page = page,
                .size = 20,
                .complianceInfoType = complianceInfoType
            }
            Dim res = Await SendRequestFlat("bg.local.goods.compliance.info.fill.list.query", req)

            If Not res("success").Value(Of Boolean)() Then
                Throw New Exception("compliance.info.fill.list.query failed: " & res.ToString())
            End If

            Dim list = TryCast(res("result")("authRepInfoList"), JArray)
            If list Is Nothing OrElse list.Count = 0 Then Exit Do

            For Each item In list
                all.Add(item)
            Next

            Dim totalToken = res("result")("total")
            Dim total As Long = If(totalToken IsNot Nothing, totalToken.Value(Of Long)(), 0L)
            If all.Count >= total Then Exit Do
            page += 1
        Loop While page < 50

        ComplianceCache(complianceInfoType) = all
        Return all
    End Function

    Public Shared Async Function ResolveApprovedRepId(complianceInfoType As Integer) As Task(Of Long?)
        Dim list = Await GetCompliancePersons(complianceInfoType)
        For Each item In list
            Dim statusTok = item("repStatus")
            If statusTok Is Nothing OrElse statusTok.Type = JTokenType.Null Then Continue For
            If statusTok.Value(Of Integer)() = 3 Then
                Dim idTok = item("repId")
                If idTok IsNot Nothing AndAlso idTok.Type <> JTokenType.Null Then
                    Return idTok.Value(Of Long)()
                End If
            End If
        Next
        Return Nothing
    End Function

    Public Shared Async Function ResolveRepIdByName(orgName As String, complianceInfoType As Integer) As Task(Of Long?)
        If String.IsNullOrWhiteSpace(orgName) Then Return Nothing

        Dim list = Await GetCompliancePersons(complianceInfoType)
        For Each item In list
            Dim statusToken = item("repStatus")
            If statusToken Is Nothing OrElse statusToken.Value(Of Integer)() <> 3 Then Continue For

            Dim repName = If(item("repName") IsNot Nothing, item("repName").ToString(), "")
            If String.Equals(repName.Trim(), orgName.Trim(), StringComparison.OrdinalIgnoreCase) Then
                Return item("repId").Value(Of Long)()
            End If
        Next
        Return Nothing
    End Function

    Private Shared Function ExtractBrandFromTitle(title As String) As String
        If String.IsNullOrWhiteSpace(title) Then Return ""
        Dim parts = title.Trim().Split(" "c)
        Return parts(0)
    End Function

    Private Shared Function ExtractTyreModel(title As String) As String
        If String.IsNullOrWhiteSpace(title) Then Return ""
        Dim t = title.Trim()
        Dim m = System.Text.RegularExpressions.Regex.Match(t, "R\d{2,3}[A-Z]?\s+(?:TL\s+)?\S+\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        If m.Success Then Return m.Groups(1).Value.Trim()
        Dim parts = t.Split(" "c)
        If parts.Length >= 3 Then Return String.Join(" ", parts.Skip(parts.Length \ 2))
        Return t
    End Function

    Private Shared Function BuildTemuTitle(originalTitle As String) As String
        Return If(originalTitle, "").Trim()
    End Function
    Private Shared Function MakeProp(refPid As Long, value As String, Optional unitId As Long = 0, Optional vid As Long = 0) As JObject
        Dim o As New JObject()
        o("refPid") = refPid
        If vid > 0 Then o("vid") = vid
        If value IsNot Nothing Then o("value") = value
        If unitId > 0 Then o("valueUnitId") = unitId
        Return o
    End Function

    Private Shared Function BuildSeasonProperty(title As String) As JObject
        Dim t = If(title, "").ToLowerInvariant()
        Dim vid As Long

        If t.Contains("winter") Then
            vid = 14237
        ElseIf t.Contains("sommer") OrElse t.Contains("summer") Then
            vid = 14236
        ElseIf t.Contains("ganzjahr") OrElse t.Contains("allwetter") OrElse t.Contains("all season") OrElse t.Contains("allseason") Then
            vid = 14240
        Else
            Return Nothing
        End If

        Return MakeProp(RefPidSeason, Nothing, 0, vid)
    End Function

    Public Shared Async Function ResolveBrandVid(brandName As String) As Task(Of Long)
        If String.IsNullOrWhiteSpace(brandName) Then Return 0

        Dim tpl = Await GetTemplate(GetCategory())
        Dim resultObj = TryCast(tpl("result"), JObject)
        If resultObj Is Nothing Then Return 0
        Dim templateInfo = TryCast(resultObj("templateInfo"), JObject)
        If templateInfo Is Nothing Then Return 0
        Dim props = TryCast(templateInfo("goodsProperties"), JArray)
        If props Is Nothing Then Return 0

        For Each prop In props
            If prop("refPid") Is Nothing OrElse prop("refPid").Value(Of Long)() <> RefPidBrand Then Continue For
            Dim values = TryCast(prop("values"), JArray)
            If values Is Nothing Then Return 0
            For Each v In values
                Dim val = If(v("value") IsNot Nothing, v("value").ToString(), "")
                If String.Equals(val.Trim(), brandName.Trim(), StringComparison.OrdinalIgnoreCase) Then
                    Return v("vid").Value(Of Long)()
                End If
            Next
        Next

        Return 0
    End Function

    Public Shared Async Function BuildGoodsProperty(p As CsvProduct) As Task(Of JArray)
        Dim arr As New JArray()

        Dim spec = TireSpec.Parse(p.Title)
        If spec Is Nothing Then
            Console.WriteLine("!! Could not parse tire size from title: " & p.Title)
            Console.WriteLine("!! Required attributes will be missing - product will NOT list.")
            Return arr
        End If

        If Not spec.IsComplete Then
            Console.WriteLine($"!! Incomplete tire spec: width={spec.WidthMm} aspect={spec.AspectRatio} rim={spec.RimInch} load={spec.LoadIndex} speed={spec.SpeedLetter}")
            Console.WriteLine("!! Title was: " & p.Title)
        End If

        Console.WriteLine($"Tire spec -> {spec.Raw} {spec.LoadIndex}{spec.SpeedLetter} | widthIn={spec.WidthInch} overallIn={spec.OverallDiameterInch}")

        Dim inv = Globalization.CultureInfo.InvariantCulture

        arr.Add(MakeProp(RefPidTireSize, spec.Raw))
        arr.Add(MakeProp(RefPidAspectRatio, spec.AspectRatio.ToString(inv), UnitPercent))
        arr.Add(MakeProp(RefPidWheelDiameter, spec.RimInch.ToString(inv), UnitInch))
        arr.Add(MakeProp(RefPidTireWidth, spec.WidthInch.ToString("0.##", inv), UnitInch))
        arr.Add(MakeProp(RefPidTireDiameter, spec.OverallDiameterInch.ToString("0.##", inv), UnitInch))

        If spec.LoadIndex > 0 Then
            arr.Add(MakeProp(RefPidLoadIndex, spec.LoadIndex.ToString(inv), UnitIndex))
        End If

        If Not String.IsNullOrEmpty(spec.SpeedLetter) Then
            Dim speedVid As Long
            If SpeedRatingVids.TryGetValue(spec.SpeedLetter, speedVid) Then
                arr.Add(MakeProp(RefPidSpeedRating, Nothing, 0, speedVid))
            Else
                Console.WriteLine("!! Unknown speed rating letter: " & spec.SpeedLetter)
            End If
        End If

        Dim seasonProp = BuildSeasonProperty(p.Title)
        If seasonProp IsNot Nothing Then arr.Add(seasonProp)

        Dim brandName = If(Not String.IsNullOrWhiteSpace(p.Brand), p.Brand.Trim(), ExtractBrandFromTitle(p.Title))
        Dim brandVid = Await ResolveBrandVid(brandName)
        If brandVid > 0 Then
            arr.Add(MakeProp(RefPidBrand, Nothing, 0, brandVid))
        Else
            Console.WriteLine("Brand '" & brandName & "' not in category value list - skipping Brand attribute")
        End If

        Console.WriteLine("goodsProperty: " & arr.ToString(Formatting.None))
        Return arr
    End Function

    Public Shared Async Function LoadExistingSkus() As Task(Of Dictionary(Of String, Long))
        Dim map As New Dictionary(Of String, Long)(StringComparer.OrdinalIgnoreCase)

        For Each statusType In New String() {"ACTIVE", "INACTIVE", "INCOMPLETE", "DRAFT"}
            Dim pageToken As String = Nothing
            Dim safety As Integer = 0
            Do
                Dim req As New JObject()
                req("pageSize") = 100
                req("skuSearchType") = statusType
                If Not String.IsNullOrEmpty(pageToken) Then req("pageToken") = pageToken

                Dim res = Await SendRequestFlat("temu.local.sku.list.retrieve", req)
                If Not res("success").Value(Of Boolean)() Then Exit Do

                Dim resultObj = TryCast(res("result"), JObject)
                If resultObj Is Nothing Then Exit Do

                Dim list = TryCast(resultObj("skuList"), JArray)
                If list IsNot Nothing Then
                    For Each sku In list
                        Dim outSkuSn = If(sku("outSkuSn") IsNot Nothing, sku("outSkuSn").ToString().Trim(), "")
                        Dim gid = sku("goodsId")
                        If Not String.IsNullOrEmpty(outSkuSn) AndAlso gid IsNot Nothing AndAlso gid.Type <> JTokenType.Null Then
                            map(outSkuSn) = Long.Parse(gid.ToString())
                        End If
                    Next
                End If

                Dim pagination = TryCast(resultObj("pagination"), JObject)
                pageToken = If(pagination IsNot Nothing AndAlso pagination("nextToken") IsNot Nothing,
                               pagination("nextToken").ToString(), Nothing)
                safety += 1
            Loop While Not String.IsNullOrEmpty(pageToken) AndAlso safety < 500
        Next

        ExistingSkuMap = map
        Console.WriteLine($"Loaded {map.Count} existing SKUs from store.")
        Return map
    End Function

    Public Shared Async Function FindExistingGoods(skuCode As String) As Task(Of Long?)
        If String.IsNullOrWhiteSpace(skuCode) Then Return Nothing
        If ExistingSkuMap Is Nothing Then Await LoadExistingSkus()

        Dim gid As Long
        If ExistingSkuMap.TryGetValue(skuCode.Trim(), gid) Then Return gid
        Return Nothing
    End Function
    Public Shared Async Function TestEnergyLabel(goodsId As Long, brand As String, model As String) As Task
        Console.WriteLine($"=== TestEnergyLabel {brand} / {model} ===")

        Dim energyLabel As New JObject()
        energyLabel("brand") = brand
        energyLabel("model") = model
        energyLabel("agreeAuthorization") = True

        Dim certDetail As New JObject()
        certDetail("certType") = 78
        certDetail("skip") = False
        certDetail("energyLabel") = energyLabel

        Dim certificateInfo As New JObject()
        certificateInfo("certificateDetailList") = New JArray(certDetail)

        Dim req As New JObject()
        req("goodsId") = goodsId
        req("certificateInfo") = certificateInfo

        Dim res = Await SendRequestFlat("bg.local.goods.compliance.edit", req)
        Console.WriteLine(res.ToString(Formatting.Indented))
    End Function
    Public Shared Async Function TestEnergyVariants(goodsId As Long) As Task
        Console.WriteLine("=== TestEnergyVariants goodsId=" & goodsId & " ===")

        Dim variants As New List(Of KeyValuePair(Of String, String))
        variants.Add(New KeyValuePair(Of String, String)("HANKOOK", "WINTER I*CEPT EVO3 X W330A"))
        variants.Add(New KeyValuePair(Of String, String)("Hankook", "WINTER I*CEPT EVO3 X W330A"))
        variants.Add(New KeyValuePair(Of String, String)("HANKOOK", "W330A"))

        For Each v In variants
            Console.WriteLine("")
            Console.WriteLine($"--- trying brand='{v.Key}' model='{v.Value}' ---")

            Dim energyLabel As New JObject()
            energyLabel("brand") = v.Key
            energyLabel("model") = v.Value
            energyLabel("agreeAuthorization") = True

            Dim certDetail As New JObject()
            certDetail("certType") = 78
            certDetail("skip") = False
            certDetail("energyLabel") = energyLabel

            Dim certificateInfo As New JObject()
            certificateInfo("certificateDetailList") = New JArray(certDetail)

            Dim req As New JObject()
            req("goodsId") = goodsId
            req("certificateInfo") = certificateInfo

            Dim res = Await SendRequestFlat("bg.local.goods.compliance.edit", req)
            If res("success").Value(Of Boolean)() Then
                Console.WriteLine($">>> SUCCESS with brand='{v.Key}' model='{v.Value}'")
            Else
                Console.WriteLine($"    FAIL [{res("errorCode")}] {res("errorMsg")}")
            End If

            Await Task.Delay(1000)
        Next

        Console.WriteLine("=== DONE ===")
    End Function
    Public Shared Async Function TestEnergyVariantsCustom(goodsId As Long, variants As List(Of KeyValuePair(Of String, String))) As Task
        Console.WriteLine("=== TestEnergyVariantsCustom goodsId=" & goodsId & " ===")

        For Each v In variants
            Console.WriteLine("")
            Console.WriteLine($"--- brand='{v.Key}' model='{v.Value}' ---")

            Dim energyLabel As New JObject()
            energyLabel("brand") = v.Key
            energyLabel("model") = v.Value
            energyLabel("agreeAuthorization") = True

            Dim certDetail As New JObject()
            certDetail("certType") = 78
            certDetail("skip") = False
            certDetail("energyLabel") = energyLabel

            Dim certificateInfo As New JObject()
            certificateInfo("certificateDetailList") = New JArray(certDetail)

            Dim req As New JObject()
            req("goodsId") = goodsId
            req("certificateInfo") = certificateInfo

            Dim res = Await SendRequestFlat("bg.local.goods.compliance.edit", req)
            If res("success").Value(Of Boolean)() Then
                Console.WriteLine($">>> SUCCESS with brand='{v.Key}' model='{v.Value}'")
            Else
                Console.WriteLine($"    FAIL [{res("errorCode")}] {res("errorMsg")}")
            End If

            Await Task.Delay(1000)
        Next

        Console.WriteLine("=== DONE ===")
    End Function
    Public Shared Async Function SetEnergyLabel(goodsId As Long, brand As String, model As String) As Task(Of Boolean)
        If String.IsNullOrWhiteSpace(brand) OrElse String.IsNullOrWhiteSpace(model) Then
            Console.WriteLine("Energy label skipped - brand/model missing")
            Return False
        End If

        Dim energyLabel As New JObject()
        energyLabel("brand") = brand.Trim()
        energyLabel("model") = model.Trim()
        energyLabel("agreeAuthorization") = True

        Dim certDetail As New JObject()
        Dim certTypeStr = ConfigurationManager.AppSettings("TemuEnergyCertType")
        Dim certTypeVal As Integer
        If Not Integer.TryParse(certTypeStr, certTypeVal) Then certTypeVal = 78
        certDetail("certType") = certTypeVal
        certDetail("skip") = False
        certDetail("energyLabel") = energyLabel

        Dim certificateInfo As New JObject()
        certificateInfo("certificateDetailList") = New JArray(certDetail)

        Dim req As New JObject()
        req("goodsId") = goodsId
        req("certificateInfo") = certificateInfo

        Dim res = Await SendRequestFlat("bg.local.goods.compliance.edit", req)
        If res("success").Value(Of Boolean)() Then
            Console.WriteLine($"Energy label OK for goodsId {goodsId}")
            Return True
        End If

        Console.WriteLine($"Energy label FAILED for goodsId {goodsId} - [{res("errorCode")}] {res("errorMsg")}")
        Return False
    End Function

    Private Shared Function ExtractSpecIdsFromDetail(detail As JObject) As JArray
        Dim result As New JArray()
        Dim resultObj = TryCast(detail("result"), JObject)
        If resultObj Is Nothing Then Return result

        For Each candidateKey In {"skuList", "skuInfoList", "goodsSkuList"}
            Dim arr = TryCast(resultObj(candidateKey), JArray)
            If arr Is Nothing OrElse arr.Count = 0 Then Continue For

            For Each item In arr
                Dim skuObj = TryCast(item, JObject)
                If skuObj Is Nothing Then Continue For

                Dim flat = TryCast(skuObj("specIdList"), JArray)
                If flat Is Nothing Then flat = TryCast(skuObj("specIds"), JArray)
                If flat IsNot Nothing AndAlso flat.Count > 0 Then Return flat

                For Each specKey In {"specList", "specDetails", "specInfoList"}
                    Dim specArr = TryCast(skuObj(specKey), JArray)
                    If specArr Is Nothing OrElse specArr.Count = 0 Then Continue For
                    For Each s In specArr
                        Dim idTok = s("specId")
                        If idTok IsNot Nothing AndAlso idTok.Type <> JTokenType.Null Then
                            result.Add(idTok.Value(Of Long)())
                        End If
                    Next
                    If result.Count > 0 Then Return result
                Next
            Next
        Next

        Return result
    End Function
    Public Shared Async Function SetGpsrOnly(goodsId As Long, manufacturerId As Long, responsiblePersonId As Long, produktId As String) As Task(Of Boolean)
        Console.WriteLine("=== SetGpsrOnly goodsId=" & goodsId & " ===")

        Dim gpsrInfo As New JObject()
        gpsrInfo("skip") = False
        gpsrInfo("manufacturerList") = New JArray(JObject.FromObject(New With {
            .repType = 3,
            .repId = manufacturerId
        }))
        gpsrInfo("responsiblePersonList") = New JArray(JObject.FromObject(New With {
            .repType = 2,
            .repId = responsiblePersonId
        }))

        Dim req As New JObject()
        req("goodsId") = goodsId
        req("gpsrInfo") = gpsrInfo

        If Not String.IsNullOrWhiteSpace(produktId) Then
            Dim multiLine As New JObject()
            multiLine("name") = produktId.Trim()

            Dim inputValue As New JObject()
            inputValue("multiLineInputs") = New JArray(multiLine)

            Dim inputText As New JObject()
            inputText("1100100115") = inputValue

            Dim extraDetail As New JObject()
            extraDetail("templateId") = 51
            extraDetail("inputText") = inputText

            Dim extraTemplate As New JObject()
            extraTemplate("extraTemplateDetailList") = New JArray(extraDetail)

            req("extraTemplate") = extraTemplate
        End If

        Console.WriteLine("PAYLOAD:")
        Console.WriteLine(req.ToString(Formatting.Indented))

        Dim res = Await SendRequestFlat("bg.local.goods.compliance.edit", req)

        If res("success").Value(Of Boolean)() Then
            Console.WriteLine("** GPSR SET OK **")
            Return True
        End If

        Console.WriteLine($"GPSR FAILED [{res("errorCode")}] {res("errorMsg")}")
        Return False
    End Function

    Public Shared Async Function LoadSpecIdsFromDetail(goodsId As Long) As Task(Of JArray)
        Dim req As New JObject()
        req("goodsId") = goodsId
        Dim res = Await SendRequestFlat("bg.local.goods.detail.query", req)
        If Not res("success").Value(Of Boolean)() Then Return New JArray()
        Return ExtractSpecIdsFromDetail(res)
    End Function

    Public Shared Async Function WaitForGoodsReady(goodsId As Long, Optional maxAttempts As Integer = 10) As Task(Of Boolean)
        For attempt = 1 To maxAttempts
            Dim req As New JObject()
            req("goodsId") = goodsId
            Dim res = Await SendRequestFlat("bg.local.goods.detail.query", req)
            If res("success").Value(Of Boolean)() Then
                Console.WriteLine($"Goods {goodsId} readable after {attempt} attempt(s)")
                Return True
            End If
            Dim ec = If(res("errorCode") IsNot Nothing, res("errorCode").ToString(), "?")
            Console.WriteLine($"Goods {goodsId} not ready yet (attempt {attempt}) [{ec}]")
            Await Task.Delay(2000)
        Next
        Return False
    End Function
    Public Shared Async Function VerifyCategory(goodsId As Long, expectedCatId As Long) As Task(Of Boolean)
        Dim req As New JObject()
        req("goodsId") = goodsId
        Dim res = Await SendRequestFlat("bg.local.goods.detail.query", req)
        If Not res("success").Value(Of Boolean)() Then Return False

        Dim resultObj = TryCast(res("result"), JObject)
        If resultObj Is Nothing OrElse resultObj("catId") Is Nothing Then Return False

        Dim actual = resultObj("catId").Value(Of Long)()
        Dim catName = If(resultObj("catName") IsNot Nothing, resultObj("catName").ToString(), "?")

        If actual = expectedCatId Then
            Console.WriteLine($"Category OK - {actual} ({catName})")
            Return True
        End If

        Console.WriteLine($"!! CATEGORY MISMATCH - got {actual} ({catName}), expected {expectedCatId}")
        Console.WriteLine("!! This product cannot list. Delete it and recreate.")
        Return False
    End Function
    Public Shared Async Function WatchCategory(goodsId As Long, Optional seconds As Integer = 120) As Task
        Console.WriteLine("=== WatchCategory goodsId=" & goodsId & " ===")
        Dim deadline = DateTime.UtcNow.AddSeconds(seconds)
        Dim lastCat As Long = -1

        While DateTime.UtcNow < deadline
            Dim req As New JObject()
            req("goodsId") = goodsId
            Dim res = Await SendRequestFlat("bg.local.goods.detail.query", req)

            If res("success").Value(Of Boolean)() Then
                Dim r = TryCast(res("result"), JObject)
                If r IsNot Nothing AndAlso r("catId") IsNot Nothing Then
                    Dim cat = r("catId").Value(Of Long)()
                    Dim props = TryCast(r("goodsProperties"), JArray)
                    Dim propCount = If(props Is Nothing, 0, props.Count)
                    Dim name = If(r("catName") IsNot Nothing, r("catName").ToString(), "?")

                    If cat <> lastCat Then
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] catId={cat} props={propCount} | {name}")
                        lastCat = cat
                    Else
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] catId={cat} props={propCount}")
                    End If
                End If
            End If

            Await Task.Delay(5000)
        End While

        Console.WriteLine("=== WatchCategory DONE ===")
    End Function
    Public Shared Async Function SubmitProduct(goodsId As Long) As Task(Of Boolean)
        Console.WriteLine("=== SubmitProduct goodsId=" & goodsId & " ===")

        Dim req As New JObject()
        req("goodsId") = goodsId
        req("saveMode") = 1

        Dim res = Await SendRequestWrapped(
    "bg.local.goods.partial.update",
    req)
        Console.WriteLine(res.ToString(Formatting.Indented))

        If res("success") IsNot Nothing AndAlso res("success").Value(Of Boolean)() Then
            Console.WriteLine("** SUBMITTED OK **")
            Return True
        End If

        Console.WriteLine($"Submit FAILED [{res("errorCode")}] {res("errorMsg")}")
        Return False
    End Function
    Public Shared Async Function SetOnSale(goodsId As Long) As Task(Of Boolean)
        Console.WriteLine("=== SetOnSale goodsId=" & goodsId & " ===")

        Dim req As New JObject()
        req("goodsId") = goodsId
        req("onsale") = 1
        req("operationType") = 1

        Dim res = Await SendRequestWrapped("bg.local.goods.sale.status.set", req)
        Console.WriteLine(res.ToString(Formatting.Indented))

        If res("success") IsNot Nothing AndAlso res("success").Value(Of Boolean)() Then
            Console.WriteLine("** ON SALE OK **")
            Return True
        End If

        Console.WriteLine($"SetOnSale FAILED [{res("errorCode")}] {res("errorMsg")}")
        Return False
    End Function
    Public Shared Async Function BackfillAllEnergyLabels() As Task
        Console.WriteLine("=== BackfillAllEnergyLabels ===")
        EprelService.LoadCache()

        Dim outDir = IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)
        Dim createdPath = IO.Path.Combine(outDir, "created.txt")
        If Not IO.File.Exists(createdPath) Then
            Console.WriteLine("no created.txt found - nothing to process")
            Return
        End If

        Dim products = Await CsvParser.DownloadAndParse()
        Dim bySku As New Dictionary(Of String, CsvProduct)(StringComparer.OrdinalIgnoreCase)
        For Each p In products
            If Not String.IsNullOrWhiteSpace(p.Sku) Then bySku(p.Sku.Trim()) = p
        Next

        Dim ok = 0, fail = 0, skip = 0
        Dim lines = IO.File.ReadAllLines(createdPath)
        Dim i = 0

        For Each line In lines
            Dim parts = line.Split(","c)
            If parts.Length < 2 Then Continue For

            Dim sku = parts(0).Trim()
            Dim goodsId As Long
            If Not Long.TryParse(parts(1).Trim(), goodsId) Then Continue For

            i += 1

            Dim p As CsvProduct = Nothing
            If Not bySku.TryGetValue(sku, p) Then
                skip += 1
                Continue For
            End If

            Dim eprelId = EprelService.ExtractEprelId(p.EnergyLabelUrl)
            If String.IsNullOrWhiteSpace(eprelId) Then
                skip += 1
                Continue For
            End If

            Dim rec = EprelService.TryGetCached(eprelId)
            If rec Is Nothing Then rec = Await EprelService.FetchAndParse(eprelId)
            If rec Is Nothing OrElse String.IsNullOrWhiteSpace(rec.Brand) OrElse String.IsNullOrWhiteSpace(rec.Model) Then
                skip += 1
                Continue For
            End If

            Console.WriteLine($"[{i}] {sku} goodsId {goodsId} -> {rec.Brand} / {rec.Model}")
            Dim done = Await SetEnergyLabel(goodsId, rec.Brand, rec.Model)
            If done Then ok += 1 Else fail += 1

            Await Task.Delay(800)
        Next

        EprelService.SaveCache()
        Console.WriteLine($"=== DONE: ok={ok} fail={fail} skip={skip} ===")
    End Function
    Public Shared Async Function BulkUploadGoodride(maxCount As Integer) As Task
        Console.WriteLine("=== BulkUploadGoodride (max " & maxCount & ") ===")

        Dim outDir = IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)
        Dim createdPath = IO.Path.Combine(outDir, "created.txt")
        Dim failedPath = IO.Path.Combine(outDir, "failed.txt")

        Dim done As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If IO.File.Exists(createdPath) Then
            For Each line In IO.File.ReadAllLines(createdPath)
                Dim sku = line.Split(","c)(0).Trim()
                If Not String.IsNullOrEmpty(sku) Then done.Add(sku)
            Next
        End If
        Console.WriteLine("Already created: " & done.Count)

        Dim products = Await CsvParser.DownloadAndParse()
        Dim goodride = products.Where(Function(p) _
            (p.Brand IsNot Nothing AndAlso p.Brand.ToUpper().Contains("GOODRIDE")) OrElse
            (p.Title IsNot Nothing AndAlso p.Title.ToUpper().Contains("GOODRIDE"))).ToList()

        Dim todo = goodride.Where(Function(p) Not done.Contains(p.Sku)).Take(maxCount).ToList()
        Console.WriteLine("GOODRIDE total: " & goodride.Count & " | to process this run: " & todo.Count)

        Dim ok = 0
        Dim fail = 0
        Dim i = 0

        For Each p In todo
            i += 1
            Console.WriteLine("")
            Console.WriteLine($"[{i}/{todo.Count}] SKU {p.Sku} - {p.Title}")

            Try
                Dim goodsId = Await ProcessAndSubmitProduct(p)
                If goodsId.HasValue Then
                    ok += 1
                    IO.File.AppendAllText(createdPath, p.Sku & "," & goodsId.Value & Environment.NewLine)
                    Console.WriteLine("  OK -> " & goodsId.Value)
                Else
                    fail += 1
                    IO.File.AppendAllText(failedPath, p.Sku & ",create-nothing" & Environment.NewLine)
                    Console.WriteLine("  FAIL - create returned Nothing")
                End If
            Catch ex As Exception
                fail += 1
                IO.File.AppendAllText(failedPath, p.Sku & "," & ex.Message.Replace(",", ";") & Environment.NewLine)
                Console.WriteLine("  ERROR: " & ex.Message)

                If ex.Message.Contains("150011001") OrElse ex.Message.Contains("150011100") Then
                    Console.WriteLine("!! Daily listing cap hit - stopping run. Resume tomorrow.")
                    Exit For
                End If
            End Try

            Await Task.Delay(2000)
        Next

        Console.WriteLine("")
        Console.WriteLine($"=== DONE this run: ok={ok} fail={fail} ===")
        Console.WriteLine("Progress saved to created.txt / failed.txt")
    End Function
    Public Shared Async Function CreateProduct(p As CsvProduct) As Task(Of TemuCreateResult)
        Try
            Console.WriteLine("Creating: " & p.Title)

            Dim catId = GetCategory()
            If catId <> 19940 Then
                Console.WriteLine("!! WARNING: TemuTireCategoryId is " & catId & ", expected 19940")
            End If
            Dim templateId = GetShippingTemplate()

            Dim manufacturerId As Long? = Await ResolveApprovedRepId(3)
            Dim euHeadId As Long? = Await ResolveApprovedRepId(2)

            Console.WriteLine($"GPSR -> manufacturerId: {If(manufacturerId.HasValue, manufacturerId.Value.ToString(), "(none)")}, responsiblePersonId: {If(euHeadId.HasValue, euHeadId.Value.ToString(), "(none)")}")

            Console.WriteLine($"GPSR -> manufacturerId: {If(manufacturerId.HasValue, manufacturerId.Value.ToString(), "(none)")}, responsiblePersonId: {If(euHeadId.HasValue, euHeadId.Value.ToString(), "(none)")}")

            Dim skuCode As String = Nothing
            If Not String.IsNullOrWhiteSpace(p.Sku) Then
                skuCode = p.Sku.Trim().Substring(0, Math.Min(p.Sku.Trim().Length, 40))
            End If

            Dim energyBrand As String = Nothing
            Dim energyModel As String = Nothing
            Dim eprelId = EprelService.ExtractEprelId(p.EnergyLabelUrl)
            If Not String.IsNullOrWhiteSpace(eprelId) Then
                Dim cached = EprelService.TryGetCached(eprelId)
                If cached Is Nothing Then cached = Await EprelService.FetchAndParse(eprelId)
                If cached IsNot Nothing Then
                    energyBrand = cached.Brand
                    energyModel = cached.Model
                End If
            End If
            If String.IsNullOrWhiteSpace(energyBrand) Then
                energyBrand = If(Not String.IsNullOrWhiteSpace(p.Brand), p.Brand.Trim(), ExtractBrandFromTitle(p.Title))
            End If
            If String.IsNullOrWhiteSpace(energyBrand) OrElse String.IsNullOrWhiteSpace(energyModel) Then
                Console.WriteLine("!! No EPREL data for " & p.Sku & " - energy label will be SKIPPED")
                energyBrand = Nothing
                energyModel = Nothing
            End If
            Console.WriteLine($"Energy label -> brand: {energyBrand}, model: {energyModel} (eprelId={eprelId})")

            Dim existingId = Await FindExistingGoods(skuCode)
            If existingId.HasValue AndAlso Not ForceRecreate Then
                Console.WriteLine($"SKIP - SKU already exists, goodsId: {existingId.Value}")
                Dim existingSpecs = Await LoadSpecIdsFromDetail(existingId.Value)
                Return New TemuCreateResult With {
                    .GoodsId = existingId.Value,
                    .OutSkuSn = skuCode,
                    .SpecIds = existingSpecs,
                    .Warnings = New List(Of String),
                    .ManufacturerId = manufacturerId,
                    .ResponsiblePersonId = euHeadId
                }
            End If
            If existingId.HasValue Then
                Console.WriteLine($"ForceRecreate - ignoring existing goodsId {existingId.Value}")
            End If

            Dim imgArr As New List(Of String)
            Dim uploadedImageMain = Await UploadImage(p.ImageUrl1, catId)

            imgArr.Add(uploadedImageMain)

            Dim uploadimage2, uploadimage3, uploadimage4, uploadimage5

            If Not String.IsNullOrWhiteSpace(p.ImageUrl2) Then
                uploadimage2 = Await UploadImage(p.ImageUrl2, catId)
                imgArr.Add(uploadimage2)
            End If
            If Not String.IsNullOrWhiteSpace(p.ImageUrl3) Then
                uploadimage3 = Await UploadImage(p.ImageUrl3, catId)
                imgArr.Add(uploadimage3)
            End If
            If Not String.IsNullOrWhiteSpace(p.ImageUrl4) Then
                uploadimage4 = Await UploadImage(p.ImageUrl4, catId)
                imgArr.Add(uploadimage4)
            End If
            If Not String.IsNullOrWhiteSpace(p.ImageUrl5) Then
                uploadimage5 = Await UploadImage(p.ImageUrl5, catId)
                imgArr.Add(uploadimage5)
            End If

            'Following Images are our advertising images, which do not need to be checked if they exist
            Dim uploadedImage6 = Await UploadImage(p.ImageUrl6, catId)
            Dim uploadedImage7 = Await UploadImage(p.ImageUrl7, catId)
            Dim uploadedImage8 = Await UploadImage(p.ImageUrl8, catId)
            Dim uploadedImage9 = Await UploadImage(p.ImageUrl9, catId)

            imgArr.Add(uploadedImage6)
            imgArr.Add(uploadedImage7)
            imgArr.Add(uploadedImage8)
            imgArr.Add(uploadedImage9)

            Dim quantity As Integer
            If Not Integer.TryParse(p.Quantity, quantity) OrElse quantity < 1 Then quantity = 10

            Dim weight As String = "10000"
            Dim weightKg As Double
            If Double.TryParse(p.Weight, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, weightKg) AndAlso weightKg > 0 Then
                weight = CInt(Math.Round(weightKg * 1000)).ToString(Globalization.CultureInfo.InvariantCulture)
            End If
            Dim price As String = If(String.IsNullOrWhiteSpace(p.Price), "10.00", p.Price.Trim())

            Dim specChildName As String = If(String.IsNullOrWhiteSpace(p.Title), "Default",
                                             p.Title.Substring(0, Math.Min(p.Title.Length, 20)))

            If specChildName.Length >= 20 Then specChildName = specChildName.Substring(0, 20)
            Dim spec = Await ResolveSpec(catId, specChildName)

            Dim taxCode = Await GetTaxCode(catId)
            Dim brandName = If(Not String.IsNullOrWhiteSpace(p.Brand), p.Brand.Trim(), ExtractBrandFromTitle(p.Title))
            Dim trademark = Await ResolveTrademark(brandName)

            Dim brandObj As New JObject()
            If trademark.trademarkId.HasValue Then
                brandObj("trademarkId") = trademark.trademarkId.Value
                brandObj("noTrademark") = False
            Else
                brandObj("noTrademark") = True
            End If

            Dim goodsBasic As New JObject()
            goodsBasic("goodsName") = BuildTemuTitle(p.Title)
            goodsBasic("catId") = catId
            goodsBasic("goodsDesc") = p.Description
            goodsBasic("bulletPoints") = New JArray(BuildTemuTitle(p.Title))
            goodsBasic("goodsGallery") = JObject.FromObject(New With {
                .goodsCarouselImage = imgArr.ToArray,
                .detailImage = imgArr.ToArray
            })
            goodsBasic("brand") = brandObj
            If skuCode IsNot Nothing Then goodsBasic("externalGoodsId") = skuCode
            If Not String.IsNullOrEmpty(taxCode) Then goodsBasic("itemTaxCode") = taxCode

            Dim businessPayload As New JObject()
            businessPayload("language") = "en"
            businessPayload("goodsBasic") = goodsBasic
            businessPayload("goodsServicePromise") = JObject.FromObject(New With {
                .shipmentLimitDay = 2,
                .fulfillmentType = 1,
                .costTemplateId = templateId
            })

            Dim goodsProperty = Await BuildGoodsProperty(p)
            If goodsProperty.Count > 0 Then businessPayload("goodsProperty") = goodsProperty

            Dim originRegion1 = GetOriginRegion1()
            Dim originRegion2 = GetOriginRegion2()
            If Not String.IsNullOrWhiteSpace(originRegion1) Then
                Dim originInfo As New JObject()
                originInfo("originRegion1") = originRegion1.Trim()
                If Not String.IsNullOrWhiteSpace(originRegion2) Then
                    originInfo("originRegion2") = originRegion2.Trim()
                End If
                businessPayload("goodsOriginInfo") = originInfo
            End If

            Dim skuObj As New JObject()
            skuObj("images") = New JArray(imgArr.ToArray)
            skuObj("quantity") = CLng(quantity)
            If skuCode IsNot Nothing Then skuObj("externalSkuId") = skuCode
            skuObj("price") = JObject.FromObject(New With {
                .basePrice = New With {
                    .amount = price,
                    .currency = "EUR"
                }
            })
            skuObj("packageInfo") = JObject.FromObject(New With {
                .weight = weight,
                .length = "30",
                .width = "30",
                .height = "30"
            })
            skuObj("specDetails") = New JArray(JObject.FromObject(New With {
                .parentSpecId = spec.parentSpecId,
                .specId = spec.specId
            }))

            ',
            '.specName = spec.specName

            If Not String.IsNullOrWhiteSpace(p.Ean) Then
                skuObj("barCodeType") = 1
                skuObj("barCodeId") = p.Ean.Trim()
            End If

            businessPayload("skuList") = New JArray(skuObj)

            Dim res = Await SendRequestFlat("temu.local.goods.v2.add", businessPayload)

            If Not res("success").Value(Of Boolean)() Then
                Console.WriteLine($"CreateProduct FAILED - [{res("errorCode")}] {res("errorMsg")}")
                Return Nothing
            End If

            Dim resultObj = TryCast(res("result"), JObject)
            If resultObj Is Nothing OrElse resultObj("goodsId") Is Nothing Then
                Console.WriteLine("CreateProduct: no goodsId in response")
                Return Nothing
            End If

            Dim goodsId = resultObj("goodsId").Value(Of Long)()
            Console.WriteLine($"SUCCESS - goodsId: {goodsId}")

            Dim outResult As New TemuCreateResult With {
                .GoodsId = goodsId,
                .OutSkuSn = skuCode,
                .SpecIds = New JArray(),
                .Warnings = New List(Of String),
                .ManufacturerId = manufacturerId,
                .ResponsiblePersonId = euHeadId,
                .EnergyBrand = energyBrand,
                .EnergyModel = energyModel
            }

            Dim skuInfoList = TryCast(resultObj("skuInfoList"), JArray)
            If skuInfoList IsNot Nothing AndAlso skuInfoList.Count > 0 Then
                Dim firstSku = TryCast(skuInfoList(0), JObject)
                If firstSku IsNot Nothing Then
                    If firstSku("skuId") IsNot Nothing Then outResult.SkuId = firstSku("skuId").Value(Of Long)()
                    If firstSku("outSkuSn") IsNot Nothing Then outResult.OutSkuSn = firstSku("outSkuSn").ToString()
                    Dim specList = TryCast(firstSku("specList"), JArray)
                    If specList IsNot Nothing Then
                        For Each s In specList
                            If s("specId") IsNot Nothing Then outResult.SpecIds.Add(s("specId").Value(Of Long)())
                        Next
                    End If
                End If
            End If

            Dim warnArr = TryCast(resultObj("warnings"), JArray)
            If warnArr IsNot Nothing Then
                For Each w In warnArr
                    If w("message") IsNot Nothing Then
                        Dim msg = w("message").ToString()
                        outResult.Warnings.Add(msg)
                        Console.WriteLine("WARNING from Temu: " & msg)
                    End If
                Next
            End If

            Console.WriteLine("specIdList from create response: " & outResult.SpecIds.ToString(Formatting.None))

            Return outResult

        Catch ex As Exception
            Console.WriteLine("ERROR creating product: " & ex.Message)
            Console.WriteLine(ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Public Shared Async Function SubmitForListing(goodsId As Long,
                                                   siteId As Long,
                                                   priceAmount As String,
                                                   listPriceAmount As String,
                                                   quantity As Long,
                                                   sku As String,
                                                   specIds As JArray) As Task(Of Boolean)
        Console.WriteLine("======== SubmitForListing (multi-site) ========")
        Console.WriteLine($"goodsId={goodsId} siteId={siteId} price={priceAmount} qty={quantity} sku={sku}")

        If specIds Is Nothing OrElse specIds.Count = 0 Then
            specIds = Await LoadSpecIdsFromDetail(goodsId)
        End If
        If specIds Is Nothing OrElse specIds.Count = 0 Then
            Console.WriteLine("ABORT - specIdList is empty.")
            Return False
        End If

        Dim basePrice As New JObject()
        basePrice("amount") = priceAmount
        basePrice("currency") = "EUR"

        Dim price As New JObject()
        price("basePrice") = basePrice
        If Not String.IsNullOrWhiteSpace(listPriceAmount) Then
            Dim listPrice As New JObject()
            listPrice("amount") = listPriceAmount
            listPrice("currency") = "EUR"
            price("listPrice") = listPrice
            price("listPriceType") = 0
        Else
            price("listPriceType") = 1
        End If

        Dim submitSku As New JObject()
        submitSku("price") = price
        submitSku("quantity") = quantity
        submitSku("specIdList") = specIds
        If Not String.IsNullOrWhiteSpace(sku) Then submitSku("outSkuSn") = sku

        Dim servicePromise As New JObject()
        servicePromise("shipmentLimitDay") = 2
        servicePromise("fulfillmentType") = 1
        servicePromise("costTemplateId") = GetShippingTemplate()

        Dim commit As New JObject()
        commit("siteId") = siteId
        commit("goodsServicePromise") = servicePromise
        commit("skuList") = New JArray(submitSku)

        Dim taxCode = Await GetTaxCode(GetCategory())
        If Not String.IsNullOrEmpty(taxCode) Then
            Dim taxInfo As New JObject()
            taxInfo("itemTaxCode") = taxCode
            commit("taxCodeInfo") = taxInfo
        End If

        Dim req As New JObject()
        req("goodsId") = goodsId
        req("commitList") = New JArray(commit)

        Dim res = Await SendRequestFlat("bg.local.goods.multi.site.submit", req)

        If Not res("success").Value(Of Boolean)() Then
            Console.WriteLine($"SubmitForListing FAILED [{res("errorCode")}] {res("errorMsg")}")
            Return False
        End If

        Dim resultObj = TryCast(res("result"), JObject)
        Dim list = If(resultObj Is Nothing, Nothing, TryCast(resultObj("resultList"), JArray))
        If list Is Nothing OrElse list.Count = 0 Then
            Console.WriteLine("SubmitForListing: envelope success but empty resultList")
            Return False
        End If

        Dim allOk As Boolean = True
        For Each r In list
            Dim ok = r("success") IsNot Nothing AndAlso r("success").Value(Of Boolean)()
            Dim sid = If(r("siteId") IsNot Nothing, r("siteId").ToString(), "?")
            If ok Then
                Console.WriteLine($"site {sid} OK -> targetGoodsId {r("targetGoodsId")}")
            Else
                allOk = False
                Console.WriteLine($"site {sid} FAILED -> {r("errorMessage")}")
            End If
        Next

        Return allOk
    End Function
    Public Shared Async Function QueryComplianceRequirements(goodsId As Long) As Task
        Console.WriteLine("=== QueryComplianceRequirements goodsId=" & goodsId & " ===")
        Dim req As New JObject()
        req("goodsId") = goodsId
        Dim res = Await SendRequestFlat("bg.local.goods.compliance.info.fill.list.query", req)
        Console.WriteLine(res.ToString(Formatting.Indented))
        Console.WriteLine("=== DONE ===")
    End Function
    Public Shared Async Function CheckComplianceStatus(goodsId As Long) As Task(Of Boolean)
        Dim req As New JObject()
        req("pageNo") = 1
        req("pageSize") = 25
        req("searchText") = goodsId.ToString()

        Dim res = Await SendRequestWrapped("bg.local.compliance.goods.list.query", req)
        If Not res("success").Value(Of Boolean)() Then
            Console.WriteLine($"CheckComplianceStatus failed [{res("errorCode")}] {res("errorMsg")}")
            Return False
        End If

        Dim resultObj = TryCast(res("result"), JObject)
        Dim list = If(resultObj Is Nothing, Nothing, TryCast(resultObj("goodsList"), JArray))
        If list Is Nothing OrElse list.Count = 0 Then
            Console.WriteLine("CheckComplianceStatus: goods not found in compliance list")
            Return False
        End If

        Dim allApproved As Boolean = True
        For Each g In list
            If g("goodsId") Is Nothing OrElse g("goodsId").Value(Of Long)() <> goodsId Then Continue For

            For Each blockName In {"extraTemplateInfoList", "gpsrInfoList", "certificateInfoList", "actualPhotoList", "repInfoList"}
                Dim arr = TryCast(g(blockName), JArray)
                If arr Is Nothing Then Continue For
                For Each item In arr
                    Dim st = If(item("status") IsNot Nothing, item("status").Value(Of Integer)(), 0)
                    Console.WriteLine($"  {blockName} -> status {st} ({DescribeComplianceStatus(st)})")
                    If st <> 2 AndAlso st <> 3 AndAlso st <> 5 Then allApproved = False
                Next
            Next
        Next

        Console.WriteLine(If(allApproved, "Compliance complete - product will proceed to review.",
                                           "Compliance INCOMPLETE - product will stay unlisted until fixed."))
        Return allApproved
    End Function

    Public Shared Async Function SetEnergyLabelWithCode(goodsId As Long, brand As String, model As String) As Task(Of Integer)
        If String.IsNullOrWhiteSpace(brand) OrElse String.IsNullOrWhiteSpace(model) Then
            Console.WriteLine("Energy label skipped - brand/model missing")
            Return -1
        End If

        Dim energyLabel As New JObject()
        energyLabel("brand") = brand.Trim()
        energyLabel("model") = model.Trim()
        energyLabel("agreeAuthorization") = True

        Dim certDetail As New JObject()
        Dim certTypeStr = ConfigurationManager.AppSettings("TemuEnergyCertType")
        Dim certTypeVal As Integer
        If Not Integer.TryParse(certTypeStr, certTypeVal) Then certTypeVal = 78
        certDetail("certType") = certTypeVal
        certDetail("skip") = False
        certDetail("energyLabel") = energyLabel

        Dim certificateInfo As New JObject()
        certificateInfo("certificateDetailList") = New JArray(certDetail)

        Dim req As New JObject()
        req("goodsId") = goodsId
        req("certificateInfo") = certificateInfo

        Dim res = Await SendRequestFlat("bg.local.goods.compliance.edit", req)

        If res("success") IsNot Nothing AndAlso res("success").Value(Of Boolean)() Then
            Console.WriteLine($"Energy label OK for goodsId {goodsId}")
            Return 0
        End If

        Dim ec As Integer = -1
        If res("errorCode") IsNot Nothing Then Integer.TryParse(res("errorCode").ToString(), ec)
        Console.WriteLine($"Energy label FAILED for goodsId {goodsId} - [{ec}] {res("errorMsg")}")
        Return ec
    End Function

    Private Shared Function DescribeComplianceStatus(status As Integer) As String
        Select Case status
            Case 1 : Return "not submitted"
            Case 2 : Return "to be reviewed"
            Case 3 : Return "reviewing"
            Case 4 : Return "action required"
            Case 5 : Return "approved"
            Case 6 : Return "rejected"
            Case 7 : Return "to be updated"
            Case Else : Return "unknown"
        End Select
    End Function

    Public Shared Async Function ProcessAndSubmitProduct(p As CsvProduct) As Task(Of Long?)
        Console.WriteLine("")
        Console.WriteLine("######## ProcessAndSubmitProduct ########")
        Console.WriteLine("SKU: " & p.Sku)
        Console.WriteLine("Title: " & p.Title)

        Console.WriteLine("=== STEP 1/4: Create product ===")
        Dim created = Await CreateProduct(p)
        If created Is Nothing Then
            Console.WriteLine("STEP 1 FAILED")
            Return Nothing
        End If
        Console.WriteLine("STEP 1 OK - goodsId = " & created.GoodsId)

        Console.WriteLine("=== STEP 2/4: Wait for registration ===")
        Await WaitForGoodsReady(created.GoodsId)

        Console.WriteLine("=== STEP 3/4: Verify category ===")
        Dim catOk = Await VerifyCategory(created.GoodsId, GetCategory())
        If Not catOk Then
            Console.WriteLine("Category wrong - skipping compliance.")
            Return created.GoodsId
        End If

        Console.WriteLine("=== STEP 4/4: Set GPSR + ProduktID ===")
        If Not created.ManufacturerId.HasValue OrElse Not created.ResponsiblePersonId.HasValue Then
            Console.WriteLine("!! Missing rep IDs - cannot set GPSR")
            Return created.GoodsId
        End If

        Dim gpsrOk = Await SetGpsrOnly(created.GoodsId,
                                       created.ManufacturerId.Value,
                                       created.ResponsiblePersonId.Value,
                                       created.OutSkuSn)
        If gpsrOk Then
            Console.WriteLine("STEP 4 OK - compliance set")

            If Not String.IsNullOrWhiteSpace(created.EnergyBrand) AndAlso Not String.IsNullOrWhiteSpace(created.EnergyModel) Then
                Console.WriteLine("=== STEP 5: Energy label ===")
                Await SetEnergyLabel(created.GoodsId, created.EnergyBrand, created.EnergyModel)
            Else
                Console.WriteLine("No EPREL data - energy label skipped")
            End If
        End If
        Console.WriteLine("######## ProcessAndSubmitProduct DONE ########")
        Return created.GoodsId
    End Function

    Public Shared Async Function SendPriceRequest(req As JObject) As Task(Of JObject)
        Return Await SendRequestFlat("bg.local.goods.priceorder.change.sku.price", req)
    End Function

    Public Shared Async Function SendDetailForPrice(req As JObject) As Task(Of JObject)
        Return Await SendRequestFlat("bg.local.goods.detail.query", req)
    End Function

    Public Shared Async Function DebugDumpSkus() As Task
        Console.WriteLine("=== DebugDumpSkus ===")

        For Each statusType In New String() {"ACTIVE", "INCOMPLETE", "DRAFT"}
            Console.WriteLine("")
            Console.WriteLine("--- status: " & statusType & " ---")

            Dim req As New JObject()
            req("pageSize") = 50
            req("skuSearchType") = statusType

            Dim res = Await SendRequestFlat("temu.local.sku.list.retrieve", req)

            If Not res("success").Value(Of Boolean)() Then
                Console.WriteLine($"  [{res("errorCode")}] {res("errorMsg")}")
                Continue For
            End If

            Dim resultObj = TryCast(res("result"), JObject)
            Dim list = If(resultObj Is Nothing, Nothing, TryCast(resultObj("skuList"), JArray))
            If list Is Nothing OrElse list.Count = 0 Then
                Console.WriteLine("  (none)")
                Continue For
            End If

            For Each sku In list
                Dim outSku = If(sku("outSkuSn") IsNot Nothing, sku("outSkuSn").ToString(), "?")
                Dim gid = If(sku("goodsId") IsNot Nothing, sku("goodsId").ToString(), "?")
                Console.WriteLine($"  SKU {outSku} -> goodsId {gid}")
            Next
        Next

        Console.WriteLine("=== DONE ===")
    End Function
    Public Shared Async Function BulkUploadAll(maxCount As Integer) As Task
        Console.WriteLine("=== BulkUploadAll (max " & maxCount & ") ===")

        Dim outDir = IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)
        Dim createdPath = IO.Path.Combine(outDir, "created.txt")
        Dim failedPath = IO.Path.Combine(outDir, "failed.txt")

        Dim done As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If IO.File.Exists(createdPath) Then
            For Each line In IO.File.ReadAllLines(createdPath)
                Dim sku = line.Split(","c)(0).Trim()
                If Not String.IsNullOrEmpty(sku) Then done.Add(sku)
            Next
        End If
        Console.WriteLine("Already created: " & done.Count)

        Dim products = Await CsvParser.DownloadAndParse()

        Dim todo = products.Where(Function(p) Not String.IsNullOrWhiteSpace(p.Sku) AndAlso Not done.Contains(p.Sku)).Take(maxCount).ToList()
        Console.WriteLine("CSV total: " & products.Count & " | to process this run: " & todo.Count)

        Dim ok = 0
        Dim fail = 0
        Dim i = 0

        For Each p In todo
            i += 1
            Console.WriteLine("")
            Console.WriteLine($"[{i}/{todo.Count}] SKU {p.Sku} - {p.Title}")

            Try
                Dim goodsId = Await ProcessAndSubmitProduct(p)
                If goodsId.HasValue Then
                    ok += 1
                    IO.File.AppendAllText(createdPath, p.Sku & "," & goodsId.Value & Environment.NewLine)
                    Console.WriteLine("  OK -> " & goodsId.Value)
                Else
                    fail += 1
                    IO.File.AppendAllText(failedPath, p.Sku & ",create-nothing" & Environment.NewLine)
                    Console.WriteLine("  FAIL - create returned Nothing")
                End If
            Catch ex As Exception
                fail += 1
                IO.File.AppendAllText(failedPath, p.Sku & "," & ex.Message.Replace(",", ";") & Environment.NewLine)
                Console.WriteLine("  ERROR: " & ex.Message)

                If ex.Message.Contains("150011001") OrElse ex.Message.Contains("150011100") Then
                    Console.WriteLine("!! Daily listing cap hit - stopping run. Resume tomorrow.")
                    Exit For
                End If
            End Try

            Await Task.Delay(2000)
        Next

        Console.WriteLine("")
        Console.WriteLine($"=== DONE this run: ok={ok} fail={fail} ===")
        Console.WriteLine("Progress saved to created.txt / failed.txt")
    End Function

    Public Shared Async Function BackfillAllGpsr() As Task
        Console.WriteLine("=== BackfillAllGpsr ===")
        Dim outDir = IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)
        Dim createdPath = IO.Path.Combine(outDir, "created.txt")
        Dim gpsrDonePath = IO.Path.Combine(outDir, "gpsr_done.txt")

        If Not IO.File.Exists(createdPath) Then
            Console.WriteLine("created.txt not found")
            Return
        End If

        Dim gpsrDone As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If IO.File.Exists(gpsrDonePath) Then
            For Each line In IO.File.ReadAllLines(gpsrDonePath)
                Dim t = line.Trim()
                If t <> "" Then gpsrDone.Add(t)
            Next
        End If

        Dim manu = Await ResolveApprovedRepId(3)
        Dim resp = Await ResolveApprovedRepId(2)
        If Not manu.HasValue OrElse Not resp.HasValue Then
            Console.WriteLine("Missing rep IDs - manu=" & manu.HasValue & " resp=" & resp.HasValue)
            Return
        End If

        Dim ok = 0, fail = 0, skip = 0
        Dim lines2 = IO.File.ReadAllLines(createdPath)
        Dim i = 0
        For Each line In lines2
            Dim parts = line.Split(","c)
            If parts.Length < 2 Then Continue For
            Dim sku = parts(0).Trim()
            Dim gidStr = parts(1).Trim()
            If sku = "" OrElse gidStr = "" Then Continue For

            If gpsrDone.Contains(gidStr) Then
                skip += 1
                Continue For
            End If

            Dim goodsId As Long
            If Not Long.TryParse(gidStr, goodsId) Then Continue For

            i += 1
            Console.WriteLine($"[{i}] GPSR goods {gidStr} (sku {sku})")

            Try
                Dim done = Await SetGpsrOnly(goodsId, manu.Value, resp.Value, sku)
                If done Then
                    ok += 1
                    IO.File.AppendAllText(gpsrDonePath, gidStr & Environment.NewLine)
                    Console.WriteLine("  OK")
                Else
                    fail += 1
                    Console.WriteLine("  FAIL")
                End If
            Catch ex As Exception
                fail += 1
                Console.WriteLine("  ERROR: " & ex.Message)
            End Try

            Await Task.Delay(500)
        Next

        Console.WriteLine($"=== DONE ok={ok} fail={fail} skip={skip} ===")
    End Function

    Public Shared Async Function SendAmountQueryV2(req As JObject) As Task(Of JObject)
        Return Await SendRequestFlat("temu.order.amount.v2.query", req)
    End Function
    Public Shared Async Function UpdateStock(goodsId As Long, skuId As Long, targetStock As Integer) As Task(Of Boolean)
        Dim target As New JObject()
        target("skuId") = skuId
        target("stockTarget") = targetStock

        Dim req As New JObject()
        req("goodsId") = goodsId
        req("stockType") = 0
        req("skuStockTargetList") = New JArray(target)
        req("requestUniqueKey") = goodsId.ToString() & "_" & skuId.ToString() & "_" & DateTime.Now.Ticks.ToString()

        Dim res = Await SendRequestFlat("bg.local.goods.stock.edit", req)

        If res("success") IsNot Nothing AndAlso res("success").Value(Of Boolean)() Then
            Dim r = TryCast(res("result"), JObject)
            If r IsNot Nothing Then
                Dim list = TryCast(r("skuStockEditStatusInfoList"), JArray)
                If list IsNot Nothing AndAlso list.Count > 0 Then
                    Dim first = TryCast(list(0), JObject)
                    If first IsNot Nothing AndAlso first("stockEditStatus") IsNot Nothing Then
                        Return first("stockEditStatus").Value(Of Boolean)()
                    End If
                End If
            End If
            Return True
        End If

        Console.WriteLine($"Stock edit FAILED goods {goodsId} sku {skuId} -> {targetStock} : [{res("errorCode")}] {res("errorMsg")}")
        Return False
    End Function

    Private Shared Function GetStockFromCsv(p As CsvProduct) As Integer
        Dim raw As String = p.Quantity
        If String.IsNullOrWhiteSpace(raw) Then Return 0
        Dim n As Integer
        If Integer.TryParse(raw.Trim(), n) Then Return n
        Return 0
    End Function

    Public Shared Async Function SyncPricesAndStock() As Task
        Console.WriteLine("=== SYNC PRICES + STOCK ===")
        Dim outDir = IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)
        Dim createdPath = IO.Path.Combine(outDir, "created.txt")
        If Not IO.File.Exists(createdPath) Then
            Console.WriteLine("created.txt not found")
            Return
        End If

        Dim products = Await CsvParser.DownloadAndParse()

        Dim minRows As Integer = 1000
        Dim cfgMin = ConfigurationManager.AppSettings("SyncMinFeedRows")
        Dim parsedMin As Integer
        If Not String.IsNullOrEmpty(cfgMin) AndAlso Integer.TryParse(cfgMin, parsedMin) Then minRows = parsedMin
        If products Is Nothing OrElse products.Count < minRows Then
            Console.WriteLine($"ABORT SYNC: feed only has {If(products Is Nothing, 0, products.Count)} rows (min {minRows}) - possible bad/partial download, not zeroing stock")
            Return
        End If

        Dim bySku As New Dictionary(Of String, CsvProduct)(StringComparer.OrdinalIgnoreCase)
        For Each pr In products
            If Not String.IsNullOrWhiteSpace(pr.Sku) Then bySku(pr.Sku.Trim()) = pr
        Next

        Dim priceOk = 0, priceFail = 0, stockOk = 0, stockFail = 0, soldOut = 0
        Dim seen As New HashSet(Of String)

        For Each line In IO.File.ReadAllLines(createdPath)
            Dim parts = line.Split(","c)
            If parts.Length < 2 Then Continue For
            Dim sku = parts(0).Trim()
            Dim gidStr = parts(1).Trim()
            If seen.Contains(gidStr) Then Continue For
            seen.Add(gidStr)

            Dim goodsId As Long
            If Not Long.TryParse(gidStr, goodsId) Then Continue For

            Dim inFeed = bySku.ContainsKey(sku)
            Dim p As CsvProduct = Nothing
            If inFeed Then p = bySku(sku)

            Dim resolved = Await TemuPriceService.ResolveSkuAndStatus(goodsId)
            Dim skuId = resolved.skuId
            If skuId = 0 Then Continue For

            Dim qty As Integer = 0
            If inFeed Then qty = GetStockFromCsv(p)

            If (Not inFeed) OrElse qty <= 0 Then
                Dim z = Await UpdateStock(goodsId, skuId, 0)
                If z Then soldOut += 1
                Console.WriteLine(sku & " -> SOLD OUT (stock 0)")
                Await Task.Delay(400)
                Continue For
            End If

            Dim price = TemuPriceService.NormalizePrice(p.Price)
            If price <> "" Then
                Dim pc = Await TemuPriceService.ChangeSkuPrice(goodsId, skuId, price, "ERP sync")
                If pc = 0 Then priceOk += 1 Else priceFail += 1
            End If

            Dim sc = Await UpdateStock(goodsId, skuId, qty)
            If sc Then stockOk += 1 Else stockFail += 1

            Await Task.Delay(400)
        Next

        Console.WriteLine($"=== SYNC DONE: priceOK={priceOk} priceFail={priceFail} stockOK={stockOk} stockFail={stockFail} soldOut={soldOut} ===")
    End Function
    Private Shared Function CentsToStr(obj As JObject, field As String) As String
        If obj Is Nothing Then Return ""
        Dim node = TryCast(obj(field), JObject)
        If node Is Nothing OrElse node("amount") Is Nothing Then Return ""
        Dim cents As Long = node("amount").Value(Of Long)()
        Return (cents / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
    End Function

    Public Shared Async Function GetOrderPrices(parentOrderSn As String) As Task(Of Dictionary(Of String, String()))
        Dim map As New Dictionary(Of String, String())(StringComparer.OrdinalIgnoreCase)
        Dim req As New JObject()
        req("parentOrderSn") = parentOrderSn
        Dim res As JObject
        Try
            res = Await SendAmountQueryV2(req)
        Catch ex As Exception
            Return map
        End Try
        If res("success") Is Nothing OrElse Not res("success").Value(Of Boolean)() Then Return map
        Dim r = TryCast(res("result"), JObject)
        If r Is Nothing Then Return map
        Dim orderList = TryCast(r("orderList"), JArray)
        If orderList Is Nothing Then Return map
        For Each o In orderList
            Dim obj = TryCast(o, JObject)
            If obj Is Nothing OrElse obj("orderSn") Is Nothing Then Continue For
            Dim sn = obj("orderSn").ToString()
            Dim vals = New String() {
                CentsToStr(obj, "unitBasePrice"),
                CentsToStr(obj, "unitRetailPriceTaxIncl"),
                CentsToStr(obj, "unitRetailPriceTaxExcl")
            }
            map(sn) = vals
        Next
        Return map
    End Function

End Class

Public Class TemuCreateResult
    Public Property GoodsId As Long
    Public Property SkuId As Long?
    Public Property OutSkuSn As String
    Public Property SpecIds As JArray
    Public Property Warnings As List(Of String)
    Public Property ManufacturerId As Long?
    Public Property ResponsiblePersonId As Long?
    Public Property EnergyBrand As String
    Public Property EnergyModel As String
End Class