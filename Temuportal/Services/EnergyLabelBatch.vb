Imports System.IO
Imports System.Text

Public Class EnergyLabelBatch

    Private Shared Function GetBatchIds() As HashSet(Of String)
        Dim ids As New HashSet(Of String)
        ids.Add("609289074751846")
        ids.Add("609289074799868")
        ids.Add("609289074750232")
        ids.Add("609289074789144")
        ids.Add("609289074742524")
        ids.Add("608842398141493")
        ids.Add("609469463409521")
        ids.Add("609194585464202")
        ids.Add("609469463391490")
        ids.Add("609194585469524")
        ids.Add("609606902369468")
        ids.Add("609057146567227")
        ids.Add("609606902336561")
        ids.Add("609606902381962")
        ids.Add("609332024458212")
        ids.Add("609332024426792")
        ids.Add("609194585508215")
        ids.Add("603953903277926")
        ids.Add("604228781239324")
        ids.Add("603679025424960")
        Return ids
    End Function

    Private Shared Function GetLogPath() As String
        Return "vivi_energy_log_" & DateTime.Now.ToString("yyyyMMdd_HHmmss") & ".txt"
    End Function

    Private Shared Async Function RunOne(sku As String, goodsId As Long, brand As String, model As String, log As StringBuilder) As Task(Of Integer)
        Dim original = Console.Out
        Dim buffer As New StringWriter()
        Dim code As Integer = -1
        Console.SetOut(buffer)
        Try
            code = Await TemuService.SetEnergyLabelWithCode(goodsId, brand, model)
        Catch ex As Exception
            buffer.WriteLine("EXCEPTION: " & ex.ToString())
            code = -1
        Finally
            Console.SetOut(original)
        End Try

        log.AppendLine("===== goodsId " & goodsId.ToString() & " =====")
        log.AppendLine("sku: " & sku)
        log.AppendLine("time: " & DateTime.Now.ToString("s"))
        log.AppendLine("brand: " & brand)
        log.AppendLine("model: " & model)
        log.AppendLine("resultCode: " & code.ToString() & If(code = 0, " (OK)", If(code = 150011053, " (Temu EPREL DB missing - for Vivi)", " (FAILED)")))
        log.AppendLine(buffer.ToString())
        log.AppendLine()
        Console.WriteLine(goodsId.ToString() & " -> " & code.ToString())
        Return code
    End Function

    Public Shared Async Function RunBatch() As Task(Of String)
        Dim wanted = GetBatchIds()
        EprelService.LoadCache()

        Dim log As New StringBuilder()
        Dim okList As New List(Of String)
        Dim viviList As New List(Of String)
        Dim failList As New List(Of String)

        log.AppendLine("Temu energy label batch - mcpneu_erp_connector")
        log.AppendLine("started: " & DateTime.Now.ToString("s"))
        log.AppendLine("products requested: " & wanted.Count.ToString())
        log.AppendLine()

        Dim outDir = IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)
        Dim createdPath = IO.Path.Combine(outDir, "created.txt")
        If Not File.Exists(createdPath) Then
            log.AppendLine("ERROR: created.txt not found at " & createdPath)
            Dim p0 = GetLogPath()
            File.WriteAllText(p0, log.ToString(), Encoding.UTF8)
            Return p0
        End If

        Dim products = Await CsvParser.DownloadAndParse()
        Dim bySku As New Dictionary(Of String, CsvProduct)(StringComparer.OrdinalIgnoreCase)
        For Each pr In products
            If Not String.IsNullOrWhiteSpace(pr.Sku) Then bySku(pr.Sku.Trim()) = pr
        Next

        Dim seen As New HashSet(Of String)
        For Each line In File.ReadAllLines(createdPath)
            Dim parts = line.Split(","c)
            If parts.Length < 2 Then Continue For
            Dim sku = parts(0).Trim()
            Dim gidStr = parts(1).Trim()
            If Not wanted.Contains(gidStr) Then Continue For
            If seen.Contains(gidStr) Then Continue For
            seen.Add(gidStr)

            Dim goodsId As Long
            If Not Long.TryParse(gidStr, goodsId) Then Continue For

            Dim p As CsvProduct = Nothing
            If Not bySku.TryGetValue(sku, p) Then
                log.AppendLine("===== goodsId " & gidStr & " =====")
                log.AppendLine("sku: " & sku)
                log.AppendLine("resultCode: skipped - sku not in current CSV")
                log.AppendLine()
                failList.Add(gidStr)
                Continue For
            End If

            Dim eprelId = EprelService.ExtractEprelId(p.EnergyLabelUrl)
            Dim rec As EprelRecord = Nothing
            If Not String.IsNullOrWhiteSpace(eprelId) Then
                rec = EprelService.TryGetCached(eprelId)
                If rec Is Nothing Then rec = Await EprelService.FetchAndParse(eprelId)
            End If

            If rec Is Nothing OrElse String.IsNullOrWhiteSpace(rec.Brand) OrElse String.IsNullOrWhiteSpace(rec.Model) Then
                log.AppendLine("===== goodsId " & gidStr & " =====")
                log.AppendLine("sku: " & sku)
                log.AppendLine("eprelId: " & If(eprelId, "(none)"))
                log.AppendLine("resultCode: skipped - no EPREL brand/model resolved")
                log.AppendLine()
                failList.Add(gidStr)
                Continue For
            End If

            Dim code = Await RunOne(sku, goodsId, rec.Brand, rec.Model, log)
            If code = 0 Then
                okList.Add(gidStr)
                File.AppendAllText("energy_done.txt", gidStr & vbCrLf)
            ElseIf code = 150011053 Then
                viviList.Add(gidStr)
            Else
                failList.Add(gidStr)
            End If

            Await Task.Delay(800)
        Next

        For Each gidStr In wanted
            If Not seen.Contains(gidStr) Then
                log.AppendLine("===== goodsId " & gidStr & " =====")
                log.AppendLine("resultCode: skipped - not in created.txt")
                log.AppendLine()
                failList.Add(gidStr)
            End If
        Next

        EprelService.SaveCache()

        log.AppendLine("===== SUMMARY =====")
        log.AppendLine("API OK (" & okList.Count.ToString() & "):")
        For Each g In okList
            log.AppendLine("  " & g)
        Next
        log.AppendLine("For Vivi - Temu EPREL DB missing, 150011053 (" & viviList.Count.ToString() & "):")
        For Each g In viviList
            log.AppendLine("  " & g)
        Next
        log.AppendLine("Other failures (" & failList.Count.ToString() & "):")
        For Each g In failList
            log.AppendLine("  " & g)
        Next

        Dim path = GetLogPath()
        File.WriteAllText(path, log.ToString(), Encoding.UTF8)
        Console.WriteLine("log written: " & path)
        Return path
    End Function

End Class