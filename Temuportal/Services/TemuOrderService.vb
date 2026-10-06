Imports System.Configuration
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

Public Class TemuOrderService

    Private Shared ReadOnly AppKey As String = ConfigurationManager.AppSettings("TemuAppKey")
    Private Shared ReadOnly AppSecret As String = ConfigurationManager.AppSettings("TemuAppSecret")
    Private Shared ReadOnly ApiUrl As String = ConfigurationManager.AppSettings("NewApiUrl")

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

    Private Shared Async Function SendRequestFlat(apiType As String, businessObj As Object) As Task(Of JObject)
        Dim accessToken = TokenStorage.AccessToken
        Dim timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()

        Dim body As New JObject()
        body("access_token") = accessToken
        body("app_key") = AppKey
        body("data_type") = "JSON"
        body("timestamp") = timestamp
        body("type") = apiType
        body("version") = "V1"

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

        Dim client As New HttpClient()
        Dim finalJson = JsonConvert.SerializeObject(body)
        Dim content = New StringContent(finalJson, Encoding.UTF8, "application/json")

        Console.WriteLine("REQUEST: " & finalJson)
        Dim response = Await client.PostAsync(ApiUrl, content)
        Dim result = Await response.Content.ReadAsStringAsync()
        Console.WriteLine("RESPONSE: " & result)
        Return JObject.Parse(result)
    End Function

    Public Shared Async Function ListOrders(Optional parentOrderStatus As Integer? = Nothing,
                                            Optional pageSize As Integer = 100,
                                            Optional pageNumber As Integer = 1) As Task(Of JArray)
        Dim req As New JObject()
        req("pageNumber") = pageNumber
        req("pageSize") = pageSize
        If parentOrderStatus.HasValue Then req("parentOrderStatus") = parentOrderStatus.Value

        Dim res = Await SendRequestFlat("bg.order.list.v2.get", req)
        If Not res("success").Value(Of Boolean)() Then Return New JArray()

        Dim resultObj = TryCast(res("result"), JObject)
        If resultObj Is Nothing Then Return New JArray()

        Dim items = TryCast(resultObj("pageItems"), JArray)
        If items Is Nothing Then Return New JArray()
        Return items
    End Function

    Public Shared Async Function GetOrderDetail(parentOrderSn As String) As Task(Of JObject)
        Dim req As New JObject()
        req("parentOrderSn") = parentOrderSn

        Dim res = Await SendRequestFlat("bg.order.detail.v2.get", req)
        If Not res("success").Value(Of Boolean)() Then Return Nothing

        Return TryCast(res("result"), JObject)
    End Function

    Public Shared Async Function GetShippingInfo(parentOrderSn As String) As Task(Of JObject)
        Dim req As New JObject()
        req("parentOrderSn") = parentOrderSn

        Dim res = Await SendRequestFlat("bg.order.shippinginfo.v2.get", req)
        If Not res("success").Value(Of Boolean)() Then Return Nothing

        Return TryCast(res("result"), JObject)
    End Function

    Public Shared Async Function DownloadAllOrders(Optional parentOrderStatus As Integer? = Nothing) As Task(Of List(Of OrderRow))
        Dim rows As New List(Of OrderRow)

        Dim pageNumber As Integer = 1
        Dim totalProcessed As Integer = 0
        Dim safety As Integer = 0

        Do
            Console.WriteLine($"Fetching page {pageNumber}...")
            Dim items = Await ListOrders(parentOrderStatus, 100, pageNumber)
            If items Is Nothing OrElse items.Count = 0 Then Exit Do

            For Each item In items
                Dim parentMap = TryCast(item("parentOrderMap"), JObject)
                If parentMap Is Nothing Then Continue For

                Dim parentOrderSn = If(parentMap("parentOrderSn") IsNot Nothing, parentMap("parentOrderSn").ToString(), "")
                If String.IsNullOrEmpty(parentOrderSn) Then Continue For

                Console.WriteLine($"-- Order {parentOrderSn} --")

                Dim detail = Await GetOrderDetail(parentOrderSn)
                Dim shipping = Await GetShippingInfo(parentOrderSn)

                Dim detailParentMap As JObject = Nothing
                Dim orderList As JArray = Nothing
                If detail IsNot Nothing Then
                    detailParentMap = TryCast(detail("parentOrderMap"), JObject)
                    orderList = TryCast(detail("orderList"), JArray)
                End If

                If orderList Is Nothing OrElse orderList.Count = 0 Then
                    orderList = TryCast(item("orderList"), JArray)
                End If

                If orderList Is Nothing Then Continue For

                For Each orderItem In orderList
                    Dim row As New OrderRow()
                    row.ParentOrderSn = parentOrderSn
                    row.ParentOrderStatus = If(parentMap("parentOrderStatus") IsNot Nothing, parentMap("parentOrderStatus").Value(Of Integer)(), 0)
                    row.ParentOrderTime = If(parentMap("parentOrderTime") IsNot Nothing, parentMap("parentOrderTime").Value(Of Long)(), 0L)
                    row.ExpectShipLatestTime = If(parentMap("expectShipLatestTime") IsNot Nothing, parentMap("expectShipLatestTime").Value(Of Long)(), 0L)
                    row.LatestDeliveryTime = If(parentMap("latestDeliveryTime") IsNot Nothing, parentMap("latestDeliveryTime").Value(Of Long)(), 0L)
                    row.OrderPaymentType = If(parentMap("orderPaymentType") IsNot Nothing, parentMap("orderPaymentType").ToString(), "")
                    row.ShippingMethod = If(parentMap("shippingMethod") IsNot Nothing, parentMap("shippingMethod").Value(Of Integer)(), 0)

                    row.OrderSn = If(orderItem("orderSn") IsNot Nothing, orderItem("orderSn").ToString(), "")
                    row.OrderStatus = If(orderItem("orderStatus") IsNot Nothing, orderItem("orderStatus").Value(Of Integer)(), 0)
                    row.OrderCreateTime = If(orderItem("orderCreateTime") IsNot Nothing, orderItem("orderCreateTime").Value(Of Long)(), 0L)
                    row.GoodsId = If(orderItem("goodsId") IsNot Nothing, orderItem("goodsId").Value(Of Long)(), 0L)
                    row.SkuId = If(orderItem("skuId") IsNot Nothing, orderItem("skuId").Value(Of Long)(), 0L)
                    Dim productList = TryCast(orderItem("productList"), JArray)
                    If productList IsNot Nothing AndAlso productList.Count > 0 Then
                        Dim firstProd = TryCast(productList(0), JObject)
                        If firstProd IsNot Nothing AndAlso firstProd("extCode") IsNot Nothing Then
                            row.ExtCode = firstProd("extCode").ToString()
                        End If
                    End If
                    row.GoodsName = If(orderItem("goodsName") IsNot Nothing, orderItem("goodsName").ToString(), "")
                    row.Spec = If(orderItem("spec") IsNot Nothing, orderItem("spec").ToString(), "")
                    row.Quantity = If(orderItem("quantity") IsNot Nothing, orderItem("quantity").Value(Of Integer)(), 0)
                    row.OriginalOrderQuantity = If(orderItem("originalOrderQuantity") IsNot Nothing, orderItem("originalOrderQuantity").Value(Of Integer)(), 0)
                    row.ThumbUrl = If(orderItem("thumbUrl") IsNot Nothing, orderItem("thumbUrl").ToString(), "")
                    row.FulfillmentType = If(orderItem("fulfillmentType") IsNot Nothing, orderItem("fulfillmentType").ToString(), "")

                    Dim packageList = TryCast(orderItem("packageSnInfo"), JArray)
                    If packageList IsNot Nothing AndAlso packageList.Count > 0 Then
                        Dim firstPkg = packageList(0)
                        row.PackageSn = If(firstPkg("packageSn") IsNot Nothing, firstPkg("packageSn").ToString(), "")
                    End If

                    If shipping IsNot Nothing Then
                        row.ReceiptName = If(shipping("receiptName") IsNot Nothing, shipping("receiptName").ToString(), "")
                        row.Mobile = If(shipping("mobile") IsNot Nothing, shipping("mobile").ToString(), "")
                        row.Mail = If(shipping("mail") IsNot Nothing, shipping("mail").ToString(), "")
                        row.RegionName1 = If(shipping("regionName1") IsNot Nothing, shipping("regionName1").ToString(), "")
                        row.RegionName2 = If(shipping("regionName2") IsNot Nothing, shipping("regionName2").ToString(), "")
                        row.RegionName3 = If(shipping("regionName3") IsNot Nothing, shipping("regionName3").ToString(), "")
                        row.AddressLine1 = If(shipping("addressLine1") IsNot Nothing, shipping("addressLine1").ToString(), "")
                        row.AddressLine2 = If(shipping("addressLine2") IsNot Nothing, shipping("addressLine2").ToString(), "")
                        row.PostCode = If(shipping("postCode") IsNot Nothing, shipping("postCode").ToString(), "")
                        row.AddressLineAll = If(shipping("addressLineAll") IsNot Nothing, shipping("addressLineAll").ToString(), "")
                    End If

                    rows.Add(row)
                    Console.WriteLine($"   item: {row.OrderSn} | {row.GoodsName} | qty={row.Quantity} | status={row.OrderStatus}")
                Next

                totalProcessed += 1
                Await Task.Delay(200)
            Next

            If items.Count < 100 Then Exit Do
            pageNumber += 1
            safety += 1
        Loop While safety < 200

        Console.WriteLine($"Downloaded {rows.Count} order rows from {totalProcessed} parent orders.")
        Return rows
    End Function

    Private Shared PendingExportSns As New List(Of String)

    Private Shared Function GetExportedLogPath() As String
        Return "exported_orders.txt"
    End Function

    Private Shared Function LoadExportedSns() As HashSet(Of String)
        Dim set1 As New HashSet(Of String)
        Dim path = GetExportedLogPath()
        If Not File.Exists(path) Then Return set1
        Dim lines = File.ReadAllLines(path)
        For Each ln In lines
            Dim t = ln.Trim()
            If t <> "" Then set1.Add(t)
        Next
        Return set1
    End Function

    Public Shared Async Function SendAmountQueryV2(req As JObject) As Task(Of JObject)
        Return Await SendRequestFlat("temu.order.amount.v2.query", req)
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

    Public Shared Async Function BuildCsvString(rows As List(Of OrderRow)) As Task(Of String)
        Dim priceCache As New Dictionary(Of String, Dictionary(Of String, String()))(StringComparer.OrdinalIgnoreCase)

        For Each r In rows
            If String.IsNullOrWhiteSpace(r.ParentOrderSn) Then Continue For
            If priceCache.ContainsKey(r.ParentOrderSn) Then Continue For
            Dim prices = Await GetOrderPrices(r.ParentOrderSn)
            priceCache(r.ParentOrderSn) = prices
            Await Task.Delay(300)
        Next

        Dim sb As New StringBuilder()
        sb.AppendLine("ParentOrderSn,OrderSn,ParentOrderStatus,OrderStatus,ParentOrderTime,OrderCreateTime,ExpectShipLatestTime,LatestDeliveryTime,GoodsId,SkuId,InternalSku,GoodsName,Spec,Quantity,OriginalOrderQuantity,ThumbUrl,ReceiptName,Mobile,Mail,RegionName1,RegionName2,RegionName3,AddressLine1,AddressLine2,PostCode,AddressLineAll,PackageSn,OrderPaymentType,FulfillmentType,ShippingMethod,UnitBasePrice,UnitRetailPriceInclVAT,UnitRetailPriceExclVAT")

        For Each r In rows
            Dim basePrice As String = ""
            Dim retailIncl As String = ""
            Dim retailExcl As String = ""
            If Not String.IsNullOrWhiteSpace(r.ParentOrderSn) AndAlso priceCache.ContainsKey(r.ParentOrderSn) Then
                Dim pm = priceCache(r.ParentOrderSn)
                If Not String.IsNullOrWhiteSpace(r.OrderSn) AndAlso pm.ContainsKey(r.OrderSn) Then
                    Dim v = pm(r.OrderSn)
                    basePrice = v(0)
                    retailIncl = v(1)
                    retailExcl = v(2)
                End If
            End If

            sb.AppendLine(String.Join(",", New String() {
            CsvEscape(r.ParentOrderSn),
            CsvEscape(r.OrderSn),
            r.ParentOrderStatus.ToString(),
            r.OrderStatus.ToString(),
            r.ParentOrderTime.ToString(),
            r.OrderCreateTime.ToString(),
            r.ExpectShipLatestTime.ToString(),
            r.LatestDeliveryTime.ToString(),
            r.GoodsId.ToString(),
            r.SkuId.ToString(),
            CsvEscape(r.ExtCode),
            CsvEscape(r.GoodsName),
            CsvEscape(r.Spec),
            r.Quantity.ToString(),
            r.OriginalOrderQuantity.ToString(),
            CsvEscape(r.ThumbUrl),
            CsvEscape(r.ReceiptName),
            CsvEscape(r.Mobile),
            CsvEscape(r.Mail),
            CsvEscape(r.RegionName1),
            CsvEscape(r.RegionName2),
            CsvEscape(r.RegionName3),
            CsvEscape(r.AddressLine1),
            CsvEscape(r.AddressLine2),
            CsvEscape(r.PostCode),
            CsvEscape(r.AddressLineAll),
            CsvEscape(r.PackageSn),
            CsvEscape(r.OrderPaymentType),
            CsvEscape(r.FulfillmentType),
            r.ShippingMethod.ToString(),
            CsvEscape(basePrice),
            CsvEscape(retailIncl),
            CsvEscape(retailExcl)
        }))
        Next

        Return sb.ToString()
    End Function

    Public Shared Async Function WriteCsv(rows As List(Of OrderRow), path As String) As Task
        File.WriteAllText(path, Await BuildCsvString(rows), Encoding.UTF8)
        Console.WriteLine($"CSV written: {path} ({rows.Count} rows)")
    End Function

    Private Shared Function FilterNewRows(rows As List(Of OrderRow), exported As HashSet(Of String)) As List(Of OrderRow)
        Dim result As New List(Of OrderRow)
        For Each r In rows
            If r.OrderSn Is Nothing OrElse r.OrderSn = "" Then Continue For
            If exported.Contains(r.OrderSn) Then Continue For
            result.Add(r)
        Next
        Return result
    End Function

    Public Shared Async Function BuildOrderCsvForFtp() As Task(Of String)
        PendingExportSns.Clear()

        Dim statusFilter As Integer? = Nothing
        Dim cfg = ConfigurationManager.AppSettings("OrdersExportStatus")
        Dim parsed As Integer
        If Not String.IsNullOrEmpty(cfg) AndAlso Integer.TryParse(cfg, parsed) Then statusFilter = parsed

        Dim allRows = Await DownloadAllOrders(statusFilter)
        If allRows Is Nothing OrElse allRows.Count = 0 Then Return ""

        Dim exported = LoadExportedSns()
        Dim newRows = FilterNewRows(allRows, exported)
        If newRows.Count = 0 Then Return ""

        For Each r In newRows
            PendingExportSns.Add(r.OrderSn)
        Next

        Return Await BuildCsvString(newRows)
    End Function

    Public Shared Sub MarkExportSuccessful()
        If PendingExportSns.Count = 0 Then Exit Sub
        Dim sb As New StringBuilder()
        For Each sn In PendingExportSns
            sb.AppendLine(sn)
        Next
        File.AppendAllText(GetExportedLogPath(), sb.ToString(), Encoding.UTF8)
        PendingExportSns.Clear()
    End Sub
    Private Shared Function CsvEscape(s As String) As String
        If s Is Nothing Then Return ""
        Dim clean = s.Replace(",", " ")
        clean = clean.Replace(vbCrLf, " ")
        clean = clean.Replace(vbCr, " ")
        clean = clean.Replace(vbLf, " ")
        clean = clean.Replace("""", "")
        While clean.Contains("  ")
            clean = clean.Replace("  ", " ")
        End While
        Return clean.Trim()
    End Function

    Public Shared Async Function GetLogisticsCompanies(Optional regionId As Long = 76) As Task(Of JObject)
        Dim req As New JObject()
        req("regionId") = regionId
        Return Await SendRequestFlat("bg.logistics.companies.get", req)
    End Function

    Public Shared Async Function GetWarehouses() As Task(Of JObject)
        Dim req As New JObject()
        Return Await SendRequestFlat("bg.logistics.warehouse.list.get", req)
    End Function

    Public Shared Async Function ConfirmShipmentSelfDelivery(parentOrderSn As String, orderSn As String, goodsId As Long, skuId As Long, quantity As Integer, carrierId As Long, trackingNumber As String, warehouseId As String) As Task(Of Boolean)
        Dim orderInfo As New JObject()
        orderInfo("parentOrderSn") = parentOrderSn
        orderInfo("orderSn") = orderSn
        If goodsId > 0 Then orderInfo("goodsId") = goodsId
        If skuId > 0 Then orderInfo("skuId") = skuId
        orderInfo("quantity") = quantity

        Dim sendReq As New JObject()
        sendReq("carrierId") = carrierId
        sendReq("trackingNumber") = trackingNumber
        sendReq("selfShippingWarehouseId") = warehouseId
        sendReq("orderSendInfoList") = New JArray(orderInfo)

        Dim req As New JObject()
        req("sendType") = 0
        req("sendRequestList") = New JArray(sendReq)

        Dim res = Await SendRequestFlat("bg.logistics.shipment.v2.confirm", req)

        If res("success") IsNot Nothing AndAlso res("success").Value(Of Boolean)() Then
            Console.WriteLine($"Shipment confirmed: {parentOrderSn} / {orderSn} -> {trackingNumber}")
            Dim resultObj = TryCast(res("result"), JObject)
            If resultObj IsNot Nothing Then
                Dim warns = TryCast(resultObj("warningMessage"), JArray)
                If warns IsNot Nothing Then
                    For Each w In warns
                        Console.WriteLine("  warning: " & w.ToString())
                    Next
                End If
            End If
            Return True
        End If

        Console.WriteLine($"Shipment FAILED {parentOrderSn} - [{res("errorCode")}] {res("errorMsg")}")
        Return False
    End Function

    Private Shared Function GetTrackingCarrierId() As Long
        Dim v = ConfigurationManager.AppSettings("TrackingCarrierId")
        Dim id As Long
        If Long.TryParse(v, id) Then Return id
        Return 0
    End Function

    Private Shared Function GetTrackingWarehouseId() As String
        Return ConfigurationManager.AppSettings("TrackingWarehouseId")
    End Function
    Private Shared Function GetTrackingFtpPath() As String
        Dim v = ConfigurationManager.AppSettings("TrackingFtpPath")
        If String.IsNullOrWhiteSpace(v) Then v = "ftp://tyretyre.de/download/tracking/McPNeu_TEMU_trcking.csv"
        Return v
    End Function

    Private Shared Function DownloadTrackingFile() As String
        Dim url = GetTrackingFtpPath()
        Dim user = ConfigurationManager.AppSettings("FTP-User")
        Dim pass = ConfigurationManager.AppSettings("FTP-Password")

        Dim sb As New StringBuilder()
        Dim request As Net.FtpWebRequest = CType(Net.FtpWebRequest.Create(url), Net.FtpWebRequest)
        request.Method = Net.WebRequestMethods.Ftp.DownloadFile
        request.Credentials = New Net.NetworkCredential(user, pass)
        request.EnableSsl = False
        request.UseBinary = False
        request.UsePassive = True
        request.Timeout = 20000
        request.ReadWriteTimeout = 20000

        Using response As Net.FtpWebResponse = CType(request.GetResponse(), Net.FtpWebResponse)
            Using reader As New IO.StreamReader(response.GetResponseStream())
                sb.Append(reader.ReadToEnd())
            End Using
        End Using
        Return sb.ToString()
    End Function

    Public Shared Async Function ProcessTrackingFromFtp() As Task(Of String)
        Dim log As New StringBuilder()
        Dim carrierId = GetTrackingCarrierId()
        Dim warehouseId = GetTrackingWarehouseId()

        If carrierId = 0 OrElse String.IsNullOrWhiteSpace(warehouseId) Then
            log.AppendLine("ABORT: TrackingCarrierId or TrackingWarehouseId not set in config")
            Return log.ToString()
        End If

       Dim content As String
        Try
            content = Await Task.Run(Function() DownloadTrackingFile())
        Catch ex As Exception
            log.AppendLine("FTP download failed: " & ex.Message)
            Return log.ToString()
        End Try

        If String.IsNullOrWhiteSpace(content) Then
            log.AppendLine("Tracking file empty or not found")
            Return log.ToString()
        End If

        Dim localPath = IO.Path.Combine(IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location), "tracking_downloaded.csv")
        IO.File.WriteAllText(localPath, content, Encoding.UTF8)
        log.AppendLine("Downloaded tracking file (" & content.Length & " bytes) -> " & localPath)

        Dim result = Await ProcessTrackingFile(localPath)
        log.AppendLine(result)
        Return log.ToString()
    End Function

    Private Shared Function ResolveCarrierId(carrierName As String) As Long
        Dim map = ConfigurationManager.AppSettings("CarrierMap")
        If Not String.IsNullOrWhiteSpace(map) Then
            For Each pair In map.Split(","c)
                Dim kv = pair.Split(":"c)
                If kv.Length = 2 AndAlso String.Equals(kv(0).Trim(), carrierName.Trim(), StringComparison.OrdinalIgnoreCase) Then
                    Dim id As Long
                    If Long.TryParse(kv(1).Trim(), id) Then Return id
                End If
            Next
        End If
        Return 0
    End Function
    Public Shared Async Function SendOrderProbe(apiType As String, req As JObject) As Task(Of JObject)
        Return Await SendRequestFlat(apiType, req)
    End Function
    Public Shared Async Function ProcessTrackingFile(localPath As String) As Task(Of String)
        Dim log As New StringBuilder()
        Dim warehouseId = GetTrackingWarehouseId()

        If String.IsNullOrWhiteSpace(warehouseId) Then
            log.AppendLine("ABORT: TrackingWarehouseId not set in config")
            Return log.ToString()
        End If

        If Not File.Exists(localPath) Then
            log.AppendLine("File not found: " & localPath)
            Return log.ToString()
        End If

        Dim allRows = Await DownloadAllOrders()
        Dim byParent As New Dictionary(Of String, OrderRow)(StringComparer.OrdinalIgnoreCase)
        For Each r In allRows
            If Not String.IsNullOrWhiteSpace(r.ParentOrderSn) AndAlso Not byParent.ContainsKey(r.ParentOrderSn.Trim()) Then byParent(r.ParentOrderSn.Trim()) = r
        Next

        Dim lines = File.ReadAllLines(localPath)
        Dim ok = 0, fail = 0, skipped = 0
        Dim first As Boolean = True
        For Each line In lines
            If String.IsNullOrWhiteSpace(line) Then Continue For
            If first Then
                first = False
                If line.StartsWith("Referenz", StringComparison.OrdinalIgnoreCase) OrElse line.Contains("TrackingNumber") Then Continue For
            End If

            Dim parts = line.Split(";"c)
            If parts.Length < 4 Then
                log.AppendLine("skip (bad line): " & line)
                Continue For
            End If

            Dim referenz = parts(0).Trim()
            Dim carrierName = parts(2).Trim()
            Dim tracking = parts(3).Trim()
            If referenz = "" OrElse tracking = "" Then Continue For

            Dim carrierId = ResolveCarrierId(carrierName)
            If carrierId = 0 Then
                log.AppendLine("FAIL: " & referenz & " -> unknown carrier '" & carrierName & "' (add to CarrierMap config)")
                fail += 1
                Continue For
            End If

            Dim row As OrderRow = Nothing
            If Not byParent.TryGetValue(referenz, row) Then
                log.AppendLine("no match for: " & referenz)
                fail += 1
                Continue For
            End If

            Dim resp = Await ConfirmShipmentWithResponse(row.ParentOrderSn, row.OrderSn, row.GoodsId, row.SkuId, If(row.Quantity > 0, row.Quantity, 1), carrierId, tracking, warehouseId)
            If resp.Contains("""success"":true") Then
                ok += 1
                log.AppendLine("OK: " & referenz & " (" & carrierName & ") -> " & tracking)
            ElseIf resp.Contains("120012004") Then
                skipped += 1
                log.AppendLine("ALREADY SHIPPED (skip): " & referenz & " (" & carrierName & ") -> " & tracking)
            Else
                fail += 1
                log.AppendLine("FAIL: " & referenz & " (" & carrierName & ") -> " & tracking & " | " & resp)
            End If
            Await Task.Delay(500)
        Next

        log.AppendLine($"DONE ok={ok} fail={fail} skipped={skipped}")
        Return log.ToString()
    End Function
    Public Shared Async Function ConfirmShipmentWithResponse(parentOrderSn As String, orderSn As String, goodsId As Long, skuId As Long, quantity As Integer, carrierId As Long, trackingNumber As String, warehouseId As String) As Task(Of String)
        Dim orderInfo As New JObject()
        orderInfo("parentOrderSn") = parentOrderSn
        orderInfo("orderSn") = orderSn
        If goodsId > 0 Then orderInfo("goodsId") = goodsId
        If skuId > 0 Then orderInfo("skuId") = skuId
        orderInfo("quantity") = quantity

        Dim sendReq As New JObject()
        sendReq("carrierId") = carrierId
        sendReq("trackingNumber") = trackingNumber
        sendReq("selfShippingWarehouseId") = warehouseId
        sendReq("orderSendInfoList") = New JArray(orderInfo)

        Dim req As New JObject()
        req("sendType") = 0
        req("sendRequestList") = New JArray(sendReq)

        Dim res = Await SendRequestFlat("bg.logistics.shipment.v2.confirm", req)
        Return res.ToString()
    End Function
End Class