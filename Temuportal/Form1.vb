Imports System.Configuration
Imports System.IO
Imports Newtonsoft.Json.Linq

Public Class Form1

    Public productRun As Boolean = False
    Private WithEvents PriceStockTimer As New System.Windows.Forms.Timer
    Private WithEvents ProductTimer As New System.Windows.Forms.Timer
    Private WithEvents OrdersTimer As New System.Windows.Forms.Timer
    Private Sub ApplyActiveStore()
        Dim store = ConfigurationManager.AppSettings("ActiveStore")
        If String.IsNullOrWhiteSpace(store) Then store = "DE"
        store = store.Trim().ToUpperInvariant()
        Dim settings = ConfigurationManager.AppSettings
        Dim prefix = store & "_"
        For Each key In settings.AllKeys
            If key.StartsWith(prefix) Then
                Dim baseKey = key.Substring(prefix.Length)
                settings(baseKey) = settings(key)
            End If
        Next
        Console.WriteLine("Active store: " & store)
    End Sub
    Private Async Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Console.WriteLine("Application started")

        ApplyActiveStore()
        TokenService.LoadToken()

        SetupOrderTrackingCheckbox()

        PriceStockTimer.Interval = TimerIntervalMs("PriceStockIntervalMinutes", 60)
        ProductTimer.Interval = TimerIntervalMs("ProductIntegrationMinutes", 60)
        OrdersTimer.Interval = TimerIntervalMs("OrdersIntervalMinutes", 30)

        PriceStockTimer.Start()
        ProductTimer.Start()
        OrdersTimer.Start()

        Console.WriteLine("Access Token Loaded:")
        Console.WriteLine(TokenStorage.AccessToken)

        'Await DoPriceStock()
        'If Not productRun Then Await DoProductIntegration()
        'Await RunOrderTrackingCycle()
    End Sub

    Private Sub SetupOrderTrackingCheckbox()
        chkOrderTracking.Checked = AppState.GetOrderTrackingEnabled()
        Console.WriteLine("Order + Tracking is " & If(chkOrderTracking.Checked, "ENABLED", "DISABLED"))
    End Sub

    Private Sub chkOrderTracking_CheckedChanged(sender As Object, e As EventArgs) Handles chkOrderTracking.CheckedChanged
        AppState.SetOrderTrackingEnabled(chkOrderTracking.Checked)
        Console.WriteLine("Order + Tracking " & If(chkOrderTracking.Checked, "ENABLED", "DISABLED"))
    End Sub

    Private Function TimerIntervalMs(key As String, defaultMin As Integer) As Integer
        Dim minutes As Integer = defaultMin
        Dim cfg As String = ConfigurationManager.AppSettings(key)
        If Not String.IsNullOrEmpty(cfg) Then Integer.TryParse(cfg, minutes)
        If minutes < 1 Then minutes = defaultMin
        Return minutes * 60 * 1000
    End Function

    Private Async Function DoPriceStock() As Task
        Try
            Console.WriteLine("=== PRICE + STOCK SYNC START " & Date.Now.ToString("s") & " ===")
            Await TemuService.SyncPricesAndStock()
            Console.WriteLine("=== PRICE + STOCK SYNC DONE ===")
        Catch ex As Exception
            Console.WriteLine("Price/stock error: " & ex.Message)
        End Try
    End Function

    Private Async Function DoProductIntegration() As Task
        productRun = True
        Try
            Console.WriteLine("=== PRODUCT INTEGRATION START " & Date.Now.ToString("s") & " ===")
            Console.WriteLine("--- Create new products ---")
            Await TemuService.BulkUploadAll(Integer.MaxValue)
            Console.WriteLine("--- GPSR backfill ---")
            Await TemuService.BackfillAllGpsr()
            Console.WriteLine("--- Energy label backfill ---")
            Await TemuService.BackfillAllEnergyLabels()
            Console.WriteLine("=== PRODUCT INTEGRATION DONE ===")
        Catch ex As Exception
            Console.WriteLine("Integration error: " & ex.Message)
        Finally
            productRun = False
        End Try
    End Function

    Private Async Function RunOrderTrackingCycle() As Task
        If Not AppState.GetOrderTrackingEnabled() Then
            Console.WriteLine("Orders + Tracking skipped - OFF for this instance")
            Return
        End If

        Dim content As String = ""
        Try
            content = Await TemuOrderService.BuildOrderCsvForFtp()
        Catch ex As Exception
            File.AppendAllText("order_upload_failed.txt", DateTime.Now.ToString("s") & ";download;" & ex.Message & vbCrLf)
        End Try

        If content <> "" Then
            Dim uploaded As String = OrderFtpUploader.UploadOrderFile(content)
            If uploaded = "" Then
                File.AppendAllText("order_upload_failed.txt", DateTime.Now.ToString("s") & ";upload" & vbCrLf)
            Else
                TemuOrderService.MarkExportSuccessful()
                File.AppendAllText("order_upload_done.txt", DateTime.Now.ToString("s") & ";" & uploaded & vbCrLf)
            End If
        End If

        Try
            Dim trackLog As String = Await TemuOrderService.ProcessTrackingFromFtp()
            File.AppendAllText("tracking_done.txt", DateTime.Now.ToString("s") & vbCrLf & trackLog & vbCrLf)
            Console.WriteLine(trackLog)
        Catch ex As Exception
            File.AppendAllText("tracking_failed.txt", DateTime.Now.ToString("s") & ";" & ex.Message & vbCrLf)
        End Try
    End Function

    Private Async Sub PriceStockTimer_Tick(sender As Object, e As EventArgs) Handles PriceStockTimer.Tick
        PriceStockTimer.Stop()
        Try
            Await DoPriceStock()
        Finally
            PriceStockTimer.Start()
        End Try
    End Sub

    Private Async Sub ProductTimer_Tick(sender As Object, e As EventArgs) Handles ProductTimer.Tick
        ProductTimer.Stop()
        Try
            If Not productRun Then
                Await DoProductIntegration()
            End If
        Finally
            ProductTimer.Start()
        End Try
    End Sub

    Private Async Sub OrdersTimer_Tick(sender As Object, e As EventArgs) Handles OrdersTimer.Tick
        OrdersTimer.Stop()
        Try
            Await RunOrderTrackingCycle()
        Finally
            OrdersTimer.Start()
        End Try
    End Sub

    Private Async Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        If productRun Then
            MessageBox.Show("Product integration already running")
            Return
        End If
        Button1.Enabled = False
        Try
            Await DoProductIntegration()
        Finally
            Button1.Enabled = True
        End Try
        MessageBox.Show("Product integration finished - check console")
    End Sub

    Private Async Sub PriceStockBtn_Click(sender As Object, e As EventArgs) Handles Button1.DoubleClick
        Await DoPriceStock()
        MessageBox.Show("Price/stock sync finished - check console")
    End Sub

    Private Async Sub GpsrBtn_Click(sender As Object, e As EventArgs) Handles GpsrBtn.Click
        GpsrBtn.Enabled = False
        Await TemuService.BackfillAllGpsr()
        GpsrBtn.Enabled = True
        MessageBox.Show("GPSR backfill done - check console for ok/fail/skip")
    End Sub

    Private Async Sub ErpelBtn_Click(sender As Object, e As EventArgs) Handles ErpelBtn.Click
        Await TemuService.CheckManufacturerCoverage()
    End Sub

    Private Async Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
        Button2.Enabled = False
        Dim content As String = ""
        Try
            content = Await TemuOrderService.BuildOrderCsvForFtp()
        Catch ex As Exception
            MessageBox.Show("Failed: " & ex.Message)
            Button2.Enabled = True
            Return
        End Try
        If String.IsNullOrEmpty(content) Then
            Button2.Enabled = True
            MessageBox.Show("No new orders.")
            Return
        End If
        Dim uploaded = OrderFtpUploader.UploadOrderFile(content)
        If uploaded <> "" Then TemuOrderService.MarkExportSuccessful()
        Button2.Enabled = True
        MessageBox.Show(If(uploaded <> "", "Uploaded: " & uploaded, "Upload FAILED"))
    End Sub

    Private Async Sub Button3_Click(sender As Object, e As EventArgs) Handles Button3.Click
        Button3.Enabled = False
        Dim req As New Newtonsoft.Json.Linq.JObject()
        req("parentOrderStatus") = 4
        req("pageSize") = 50
        Dim res = Await TemuOrderService.SendOrderProbe("bg.order.list.v2.get", req)
        Dim sb As New System.Text.StringBuilder()
        Dim r = TryCast(res("result"), Newtonsoft.Json.Linq.JObject)
        If r IsNot Nothing Then
            Dim items = TryCast(r("pageItems"), Newtonsoft.Json.Linq.JArray)
            If items IsNot Nothing Then
                For Each it In items
                    Dim pm = TryCast(it("parentOrderMap"), Newtonsoft.Json.Linq.JObject)
                    If pm IsNot Nothing Then sb.AppendLine(pm("parentOrderSn").ToString() & " status=" & pm("parentOrderStatus").ToString())
                Next
            End If
        End If
        Button3.Enabled = True
        MessageBox.Show("SHIPPED orders:" & vbCrLf & sb.ToString())
    End Sub

    Private Async Sub btnEnergyBatch_Click(sender As Object, e As EventArgs) Handles btnEnergyBatch.Click
        btnEnergyBatch.Enabled = False
        Await TemuService.BackfillAllEnergyLabels()
        btnEnergyBatch.Enabled = True
        MessageBox.Show("Backfill done - check console for ok/fail/skip counts")
    End Sub

    Private Async Sub FrBtn_Click(sender As Object, e As EventArgs) Handles FrBtn.Click
        Await TemuService.CrawlFrCategories()
    End Sub
End Class