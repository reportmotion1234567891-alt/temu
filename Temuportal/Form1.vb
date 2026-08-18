Imports System.Configuration
Imports System.IO
Imports Newtonsoft.Json.Linq

Public Class Form1

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Console.WriteLine("Application started")

        TokenService.LoadToken()

        Console.WriteLine("Access Token Loaded:")
        Console.WriteLine(TokenStorage.AccessToken)
    End Sub

    Private Async Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        Button1.Enabled = False
        Await TemuService.BulkUploadAll(500)
        Button1.Enabled = True
        MessageBox.Show("Bulk run done - check console for ok/fail")
    End Sub
    Private Async Sub GpsrBtn_Click(sender As Object, e As EventArgs) Handles GpsrBtn.Click
        GpsrBtn.Enabled = False
        Await TemuService.BackfillAllGpsr()
        GpsrBtn.Enabled = True
        MessageBox.Show("GPSR backfill done - check console for ok/fail/skip")
    End Sub
    Private Async Sub ErpelBtn_Click(sender As Object, e As EventArgs) Handles ErpelBtn.Click
        ErpelBtn.Enabled = False
        Dim req As New Newtonsoft.Json.Linq.JObject()
        req("parentOrderSn") = "PO-076-16345271332471807"
        Dim res = Await TemuService.SendAmountQueryV2(req)
        ErpelBtn.Enabled = True
        MessageBox.Show(res.ToString())
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
    Private WithEvents OrdersTimer As New System.Windows.Forms.Timer

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
    Private Sub StartOrdersTimer()
        Dim minutes As Integer = 30
        Dim cfg As String = System.Configuration.ConfigurationManager.AppSettings("OrdersIntervalMinutes")
        If Not String.IsNullOrEmpty(cfg) Then Integer.TryParse(cfg, minutes)
        If minutes < 1 Then minutes = 30
        OrdersTimer.Interval = minutes * 60 * 1000
        OrdersTimer.Start()
    End Sub

    Private Sub OrdersTimer_Tick(sender As Object, e As EventArgs) Handles OrdersTimer.Tick
        OrdersTimer.Stop()
        RunOrderExportCycle()
    End Sub

    Private Async Sub RunOrderExportCycle()
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

        OrdersTimer.Start()
    End Sub

    Private Async Sub btnEnergyBatch_Click(sender As Object, e As EventArgs) Handles btnEnergyBatch.Click
        btnEnergyBatch.Enabled = False
        Await TemuService.BackfillAllEnergyLabels()
        btnEnergyBatch.Enabled = True
        MessageBox.Show("Backfill done - check console for ok/fail/skip counts")
    End Sub
End Class