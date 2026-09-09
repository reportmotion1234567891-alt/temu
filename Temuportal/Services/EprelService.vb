Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Text.RegularExpressions
Imports Newtonsoft.Json
Imports UglyToad.PdfPig

Public Class EprelRecord
    Public Property Brand As String
    Public Property Model As String
End Class

Public Class EprelService
    Private Shared ReadOnly client As HttpClient = CreateClient()
    Private Shared ReadOnly cacheFile As String = "eprel_cache.json"
    Private Shared cache As Dictionary(Of String, EprelRecord) = Nothing

    Private Shared Function CreateClient() As HttpClient
        Dim handler As New HttpClientHandler()
        handler.AutomaticDecompression = Net.DecompressionMethods.GZip Or Net.DecompressionMethods.Deflate
        Dim c As New HttpClient(handler)
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36")
        c.DefaultRequestHeaders.Accept.ParseAdd("application/pdf,application/octet-stream,*/*")
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9")
        c.Timeout = TimeSpan.FromSeconds(30)
        Return c
    End Function

    Public Shared Sub LoadCache()
        cache = New Dictionary(Of String, EprelRecord)()
        If File.Exists(cacheFile) Then
            Try
                Dim json = File.ReadAllText(cacheFile)
                Dim loaded = JsonConvert.DeserializeObject(Of Dictionary(Of String, EprelRecord))(json)
                If loaded IsNot Nothing Then cache = loaded
            Catch ex As Exception
                Console.WriteLine("EprelService.LoadCache failed: " & ex.Message)
            End Try
        End If
        Console.WriteLine("EprelService cache loaded with " & cache.Count & " entries.")
    End Sub

    Public Shared Sub SaveCache()
        Try
            Dim json = JsonConvert.SerializeObject(cache, Formatting.Indented)
            File.WriteAllText(cacheFile, json)
        Catch ex As Exception
            Console.WriteLine("EprelService.SaveCache failed: " & ex.Message)
        End Try
    End Sub

    Public Shared Function TryGetCached(eprelId As String) As EprelRecord
        If cache Is Nothing Then LoadCache()
        If cache.ContainsKey(eprelId) Then Return cache(eprelId)
        Return Nothing
    End Function

    Public Shared Function ExtractEprelId(labelUrl As String) As String
        If String.IsNullOrWhiteSpace(labelUrl) Then Return Nothing
        Dim m = Regex.Match(labelUrl, "Label_(\d+)\.png", RegexOptions.IgnoreCase)
        If m.Success Then Return m.Groups(1).Value
        Return Nothing
    End Function

    Public Shared Async Function FetchAndParse(eprelId As String) As Task(Of EprelRecord)
        Dim url = "https://eprel.ec.europa.eu/fiches/tyres/Fiche_" & eprelId & "_EN.pdf"

        Dim pdfBytes As Byte() = Nothing
        Try
            pdfBytes = Await client.GetByteArrayAsync(url)
        Catch ex As Exception
            Console.WriteLine("  download failed for " & eprelId & ": " & ex.Message)
            Return Nothing
        End Try

        If pdfBytes Is Nothing OrElse pdfBytes.Length < 100 Then
            Console.WriteLine("  empty/tiny PDF for " & eprelId)
            Return Nothing
        End If

        Dim text As String = ""
        Try
            text = ExtractPdfText(pdfBytes)
        Catch ex As Exception
            Console.WriteLine("  PDF parse failed for " & eprelId & ": " & ex.Message)
            Return Nothing
        End Try

        Dim rec = ParseFiche(text)
        If rec Is Nothing OrElse String.IsNullOrWhiteSpace(rec.Brand) OrElse String.IsNullOrWhiteSpace(rec.Model) Then
            Console.WriteLine("  incomplete parse for " & eprelId & " - discarding")
            Return Nothing
        End If
        Return rec
    End Function

    Private Shared Function ExtractPdfText(pdfBytes As Byte()) As String
        Dim sb As New StringBuilder()
        Using ms As New MemoryStream(pdfBytes)
            Using doc = PdfDocument.Open(ms)
                For Each page In doc.GetPages()
                    sb.AppendLine(page.Text)
                Next
            End Using
        End Using
        Return sb.ToString()
    End Function
    Public Shared Async Function DumpFiche(eprelId As String) As Task
        Dim url = "https://eprel.ec.europa.eu/fiches/tyres/Fiche_" & eprelId & "_EN.pdf"
        Console.WriteLine("=== DumpFiche " & eprelId & " ===")

        Dim pdfBytes As Byte()
        Try
            pdfBytes = Await client.GetByteArrayAsync(url)
        Catch ex As Exception
            Console.WriteLine("download failed: " & ex.Message)
            Return
        End Try

        Dim text = ExtractPdfText(pdfBytes)
        Dim normalized = text.Replace(vbCrLf, "").Replace(vbLf, "").Replace(vbCr, "")
        normalized = Regex.Replace(normalized, " {2,}", " ").Trim()

        Console.WriteLine("--- RAW NORMALIZED TEXT ---")
        Console.WriteLine(normalized)
        Console.WriteLine("--- END ---")
    End Function
    Private Shared Function ParseFiche(text As String) As EprelRecord
        If String.IsNullOrWhiteSpace(text) Then Return Nothing

        Dim normalized = text.Replace(vbCrLf, "").Replace(vbLf, "").Replace(vbCr, "")
        normalized = Regex.Replace(normalized, " {2,}", " ").Trim()

        Dim brand As String = Nothing
        Dim model As String = Nothing

        ' ===== Brand =====
        Dim brandPatterns As String() = {
            "Supplier name or trademark(.+?)Commercial name or trade designation",
            "Supplier name or trademark(.+?)Tyre type identifier",
            "Supplier name or trademark(.+?)Tyre class"
        }
        For Each pat In brandPatterns
            Dim m = Regex.Match(normalized, pat, RegexOptions.IgnoreCase)
            If m.Success Then
                brand = m.Groups(1).Value.Trim()
                If Not String.IsNullOrWhiteSpace(brand) AndAlso brand.Length < 40 Then Exit For
                brand = Nothing
            End If
        Next

        If Not String.IsNullOrWhiteSpace(brand) AndAlso brand.Contains(" ") Then
            Dim parts = brand.Split(" "c)
            Dim head = parts(0).Trim()
            Dim tail = brand.Substring(head.Length).Trim()
            If head.Length >= 4 AndAlso Not String.IsNullOrWhiteSpace(tail) Then
                brand = head
            End If
        End If

        Dim mC = Regex.Match(normalized, "Commercial name or trade designation(.+?)Tyre type identifierTyre class(\d+)C[123]Tyre size", RegexOptions.IgnoreCase)
        If mC.Success Then
            Dim name = mC.Groups(1).Value.Trim()
            Dim ident = mC.Groups(2).Value.Trim()
            model = (name & " " & ident).Trim()
        End If

        If String.IsNullOrWhiteSpace(model) Then
            Dim mA = Regex.Match(normalized, "Commercial name or trade designationTyre type identifierTyre class(.+?)C[123]Tyre size", RegexOptions.IgnoreCase)
            If mA.Success Then model = mA.Groups(1).Value.Trim()
        End If

        If String.IsNullOrWhiteSpace(model) Then
            Dim mB = Regex.Match(normalized, "Supplier name or trademark.+?Tyre type identifierTyre class(.+?)C[123]Tyre size", RegexOptions.IgnoreCase)
            If mB.Success Then model = mB.Groups(1).Value.Trim()
        End If

        If Not String.IsNullOrWhiteSpace(model) AndAlso model.Length > 60 Then model = Nothing

        If String.IsNullOrWhiteSpace(brand) OrElse String.IsNullOrWhiteSpace(model) Then
            Console.WriteLine("  parse: could not extract brand/model")
            Return Nothing
        End If

        Dim r As New EprelRecord()
        r.Brand = brand
        r.Model = model
        Return r
    End Function

    Public Shared Async Function BulkPopulate(eprelIds As List(Of String), Optional delayMs As Integer = 200) As Task
        If cache Is Nothing Then LoadCache()

        Dim total = eprelIds.Count
        Dim ok = 0
        Dim skip = 0
        Dim fail = 0
        Dim i = 0

        For Each id In eprelIds
            i += 1
            If cache.ContainsKey(id) Then
                skip += 1
                If i Mod 100 = 0 Then Console.WriteLine($"[{i}/{total}] cached, skipped: " & id)
                Continue For
            End If

            Console.WriteLine($"[{i}/{total}] fetching EPREL " & id)
            Dim rec = Await FetchAndParse(id)

            If rec IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(rec.Brand) AndAlso Not String.IsNullOrWhiteSpace(rec.Model) Then
                cache(id) = rec
                ok += 1
                Console.WriteLine("  -> brand=" & rec.Brand & " | model=" & rec.Model)
            Else
                fail += 1
                Console.WriteLine("  -> FAILED to parse EPREL " & id)
            End If

            If i Mod 50 = 0 Then
                SaveCache()
                Console.WriteLine("  (intermediate save at " & i & ")")
            End If

            Await Task.Delay(delayMs)
        Next

        SaveCache()
        Console.WriteLine("=== BulkPopulate done. total=" & total & " ok=" & ok & " skipped=" & skip & " fail=" & fail & " ===")
    End Function


End Class