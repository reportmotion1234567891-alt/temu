Imports System.Configuration
Imports System.Net

Public Class FtpService

    Private Shared ReadOnly FtpServer As String = ConfigurationManager.AppSettings("FTP-Server")
    Private Shared ReadOnly FtpUser As String = ConfigurationManager.AppSettings("FTP-User")
    Private Shared ReadOnly FtpPassword As String = ConfigurationManager.AppSettings("FTP-Password")
    Private Shared ReadOnly SockFile As String = ConfigurationManager.AppSettings("SockFile")

    Public Shared Async Function DownloadAndLogCsv() As Task
        Await Task.Run(Sub()
                           Try
                               Dim url = FtpServer & SockFile
                               Console.WriteLine("Connecting to FTP: " & url)

                               Dim request As FtpWebRequest = CType(FtpWebRequest.Create(url), FtpWebRequest)
                               request.Method = WebRequestMethods.Ftp.DownloadFile
                               request.Credentials = New NetworkCredential(FtpUser, FtpPassword)
                               request.EnableSsl = False
                               request.UseBinary = False
                               request.UsePassive = True

                               Using response As FtpWebResponse = CType(request.GetResponse(), FtpWebResponse)
                                   Console.WriteLine("FTP Status: " & response.StatusDescription.Trim())
                                   Using reader As New IO.StreamReader(response.GetResponseStream())
                                       Dim lineCount = 0
                                       While Not reader.EndOfStream AndAlso lineCount < 5
                                           Dim line = reader.ReadLine()
                                           Console.WriteLine("Line " & lineCount & ": " & line)
                                           lineCount += 1
                                       End While
                                   End Using
                               End Using

                           Catch ex As Exception
                               Console.WriteLine("FTP Error: " & ex.Message)
                           End Try
                       End Sub)
    End Function

End Class