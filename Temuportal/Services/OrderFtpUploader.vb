Imports System.Net
Imports System.IO
Imports System.Text
Imports System.Configuration

Public Class OrderFtpUploader

    Private Shared Function GetFtpHost() As String
        Dim v As String = ConfigurationManager.AppSettings("OrderFtpHost")
        If String.IsNullOrEmpty(v) Then v = "ftp://tyretyre.de"
        If v.EndsWith("/") Then v = v.Substring(0, v.Length - 1)
        Return v
    End Function

    Private Shared Function GetFtpFolder() As String
        Dim v As String = ConfigurationManager.AppSettings("OrderFtpFolder")
        If String.IsNullOrEmpty(v) Then v = "/upload/order/"
        If Not v.EndsWith("/") Then v = v & "/"
        Return v
    End Function

    Private Shared Function GetFtpUser() As String
        Dim v As String = ConfigurationManager.AppSettings("OrderFtpUser")
        If String.IsNullOrEmpty(v) Then v = "Temu"
        Return v
    End Function

    Private Shared Function GetFtpPassword() As String
        Return ConfigurationManager.AppSettings("OrderFtpPassword")
    End Function

    Private Shared Function BuildFileName(ByVal suffix As Integer) As String
        Dim stamp As String = DateTime.Now.ToString("yyyyMMdd_HHmmss")
        If suffix = 0 Then Return "temu_orders_" & stamp & ".csv"
        Return "temu_orders_" & stamp & "_" & suffix.ToString() & ".csv"
    End Function

    Private Shared Function RemoteFileExists(ByVal remoteUrl As String) As Boolean
        Dim req As FtpWebRequest = Nothing
        Dim resp As FtpWebResponse = Nothing
        Try
            req = CType(WebRequest.Create(remoteUrl), FtpWebRequest)
            req.Method = WebRequestMethods.Ftp.GetFileSize
            req.Credentials = New NetworkCredential(GetFtpUser(), GetFtpPassword())
            req.UsePassive = True
            req.KeepAlive = False
            resp = CType(req.GetResponse(), FtpWebResponse)
            resp.Close()
            Return True
        Catch ex As WebException
            If resp IsNot Nothing Then resp.Close()
            Return False
        End Try
    End Function

    Private Shared Function UploadBytes(ByVal remoteUrl As String, ByVal data As Byte()) As Boolean
        Dim req As FtpWebRequest = Nothing
        Dim stream As Stream = Nothing
        Dim resp As FtpWebResponse = Nothing
        Try
            req = CType(WebRequest.Create(remoteUrl), FtpWebRequest)
            req.Method = WebRequestMethods.Ftp.UploadFile
            req.Credentials = New NetworkCredential(GetFtpUser(), GetFtpPassword())
            req.UseBinary = True
            req.UsePassive = True
            req.KeepAlive = False
            req.ContentLength = data.Length
            stream = req.GetRequestStream()
            stream.Write(data, 0, data.Length)
            stream.Close()
            resp = CType(req.GetResponse(), FtpWebResponse)
            resp.Close()
            Return True
        Catch ex As Exception
            If stream IsNot Nothing Then stream.Close()
            Return False
        End Try
    End Function

    Public Shared Function UploadOrderFile(ByVal content As String) As String
        Dim baseUrl As String = GetFtpHost() & GetFtpFolder()
        Dim name As String = ""
        Dim i As Integer = 0
        Do While i < 50
            name = BuildFileName(i)
            If Not RemoteFileExists(baseUrl & name) Then Exit Do
            i = i + 1
        Loop
        If i >= 50 Then Return ""
        Dim data As Byte() = New UTF8Encoding(False).GetBytes(content)
        If UploadBytes(baseUrl & name, data) Then Return name
        Return ""
    End Function

End Class