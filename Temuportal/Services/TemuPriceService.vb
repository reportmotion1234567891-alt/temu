Imports System.Configuration
Imports System.IO
Imports System.Linq
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

Public Class TemuPriceService

    Private Shared Function NormalizePrice(raw As String) As String
        If String.IsNullOrWhiteSpace(raw) Then Return ""
        Return raw.Trim().Replace(",", ".")
    End Function

    Public Shared Async Function ChangeSkuPrice(goodsId As Long, skuId As Long, amount As String, reason As String) As Task(Of Integer)
        Dim newPrice As New JObject()
        newPrice("amount") = amount
        newPrice("currency") = "EUR"

        Dim baseDto As New JObject()
        baseDto("skuId") = skuId
        baseDto("newSupplierPrice") = newPrice

        Dim changeDto As New JObject()
        changeDto("reason") = reason
        changeDto("skuChangePriceBaseDTOList") = New JArray(baseDto)

        Dim req As New JObject()
        req("goodsId") = goodsId
        req("changeSkuPriceDTOList") = New JArray(changeDto)
        req("rejectSkuPricing") = False

        Dim res = Await TemuService.SendPriceRequest(req)

        If res("success") IsNot Nothing AndAlso res("success").Value(Of Boolean)() Then
            Dim resultObj = TryCast(res("result"), JObject)
            Dim failed = If(resultObj Is Nothing, Nothing, TryCast(resultObj("failedSkuList"), JArray))
            If failed IsNot Nothing AndAlso failed.Count > 0 Then
                Dim reasonMap = TryCast(resultObj("failedSkuReasonMap"), JObject)
                Dim rtext = If(reasonMap Is Nothing, "", reasonMap.ToString(Formatting.None))
                Console.WriteLine($"Price change rejected for sku {skuId}: {rtext}")
                Return -2
            End If
            Console.WriteLine($"Price change OK goods {goodsId} sku {skuId} -> {amount}")
            Return 0
        End If

        Dim ec As Integer = -1
        If res("errorCode") IsNot Nothing Then Integer.TryParse(res("errorCode").ToString(), ec)
        Console.WriteLine($"Price change FAILED goods {goodsId} sku {skuId} - [{ec}] {res("errorMsg")}")
        Return ec
    End Function

    Public Shared Async Function ResolveSkuAndStatus(goodsId As Long) As Task(Of (skuId As Long, subStatus As Integer))
        Dim req As New JObject()
        req("goodsId") = goodsId
        Dim res = Await TemuService.SendDetailForPrice(req)
        If Not res("success").Value(Of Boolean)() Then Return (0L, 0)

        Dim resultObj = TryCast(res("result"), JObject)
        If resultObj Is Nothing Then Return (0L, 0)

        Dim sub1 As Integer = If(resultObj("subStatus") IsNot Nothing, resultObj("subStatus").Value(Of Integer)(), 0)

        Dim skuList = TryCast(resultObj("skuList"), JArray)
        If skuList Is Nothing OrElse skuList.Count = 0 Then Return (0L, sub1)
        Dim first = TryCast(skuList(0), JObject)
        If first Is Nothing OrElse first("skuId") Is Nothing Then Return (0L, sub1)
        Return (first("skuId").Value(Of Long)(), sub1)
    End Function

    Public Shared Async Function UpdateAllPricesFromCsv() As Task(Of String)
        Dim log As New System.Text.StringBuilder()
        Dim reason As String = ConfigurationManager.AppSettings("PriceChangeReason")
        If String.IsNullOrWhiteSpace(reason) Then reason = "ERP price sync"

        log.AppendLine("Temu price update run")
        log.AppendLine("started: " & DateTime.Now.ToString("s"))
        log.AppendLine()

        Dim outDir = IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)
        Dim createdPath = IO.Path.Combine(outDir, "created.txt")
        If Not File.Exists(createdPath) Then
            log.AppendLine("ERROR: created.txt not found")
            Return WriteLog(log)
        End If

        Dim products = Await CsvParser.DownloadAndParse()
        Dim bySku As New Dictionary(Of String, CsvProduct)(StringComparer.OrdinalIgnoreCase)
        For Each pr In products
            If Not String.IsNullOrWhiteSpace(pr.Sku) Then bySku(pr.Sku.Trim()) = pr
        Next

        Dim ok = 0, fail = 0, skip = 0, inReview = 0
        Dim seen As New HashSet(Of String)

        For Each line In File.ReadAllLines(createdPath)
            Dim parts = line.Split(","c)
            If parts.Length < 2 Then Continue For
            Dim sku = parts(0).Trim()
            Dim gidStr = parts(1).Trim()
            If seen.Contains(gidStr) Then Continue For
            seen.Add(gidStr)

            Dim goodsId As Long
            If Not Long.TryParse(gidStr, goodsId) Then Continue For

            Dim p As CsvProduct = Nothing
            If Not bySku.TryGetValue(sku, p) Then
                skip += 1
                Continue For
            End If

            Dim amount = NormalizePrice(p.Price)
            If amount = "" Then
                skip += 1
                Continue For
            End If

            Dim resolved = Await ResolveSkuAndStatus(goodsId)
            If resolved.skuId = 0 Then
                log.AppendLine(sku & " goods " & gidStr & " -> no skuId")
                fail += 1
                Continue For
            End If

            If resolved.subStatus = 302 Then
                log.AppendLine(sku & " goods " & gidStr & " -> skipped (in review, subStatus 302)")
                inReview += 1
                Continue For
            End If

            Dim code = Await ChangeSkuPrice(goodsId, resolved.skuId, amount, reason)
            If code = 0 Then
                ok += 1
                log.AppendLine(sku & " goods " & gidStr & " sku " & resolved.skuId.ToString() & " -> " & amount & " OK")
            Else
                fail += 1
                log.AppendLine(sku & " goods " & gidStr & " sku " & resolved.skuId.ToString() & " -> " & amount & " FAIL code " & code.ToString())
            End If

            Await Task.Delay(500)
        Next

        log.AppendLine()
        log.AppendLine($"DONE ok={ok} fail={fail} skip={skip} inReview={inReview}")
        Return WriteLog(log)
    End Function

    Private Shared Function WriteLog(log As System.Text.StringBuilder) As String
        Dim path = "price_update_" & DateTime.Now.ToString("yyyyMMdd_HHmmss") & ".txt"
        File.WriteAllText(path, log.ToString(), System.Text.Encoding.UTF8)
        Console.WriteLine("log written: " & path)
        Return path
    End Function

End Class