Imports System.Configuration
Imports System.Security.Cryptography
Imports System.Text
Imports System.Net.Http
Imports System.Collections.Generic
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

Public Class TokenService

    Private Shared ReadOnly ApiUrl As String = ConfigurationManager.AppSettings("NewApiUrl")
    Private Shared ReadOnly AppKey As String = ConfigurationManager.AppSettings("TemuAppKey")
    Private Shared ReadOnly Secret As String = ConfigurationManager.AppSettings("TemuAppSecret")

    Public Shared Sub LoadToken()
        TokenStorage.AccessToken = ConfigurationManager.AppSettings("TemuAccessToken")
        TokenStorage.MallId = ConfigurationManager.AppSettings("TemuMallId")
        Console.WriteLine("Token loaded.")
        CheckTokenExpiry()
    End Sub

    Public Shared Sub CheckTokenExpiry()
        Dim ExpiryStr = ConfigurationManager.AppSettings("TemuTokenExpiry")
        Dim warningDays = Integer.Parse(If(ConfigurationManager.AppSettings("TokenWarningDays"), "30"))
        Dim expiry As Date

        If Date.TryParse(ExpiryStr, expiry) Then
            Dim daysLeft = (expiry - Date.Today).Days
            If daysLeft <= 0 Then
                Console.Write("!!! TOKEN HAS EXPIRED- renew immediately in Temu Seller Center !!!")
            ElseIf daysLeft <= warningDays Then
                Console.WriteLine("!! WARNING: Token expires in " & daysLeft & " days (" & expiry.ToString("dd.MM.yyyy") & ") — please renew soon !!")
            Else
                Console.WriteLine("Token valid for " & daysLeft & " more days (expires " & expiry.ToString("dd.MM.yyyy") & ")")
            End If
        End If
    End Sub

    Private Shared Function GenerateSign(parameters As SortedDictionary(Of String, String), secret As String) As String
        Dim sb As New StringBuilder()
        sb.Append(secret)
        For Each p In parameters
            sb.Append(p.Key).Append(p.Value)
        Next
        sb.Append(secret)

        Dim raw = sb.ToString()
        Console.WriteLine("Sign input: " & raw)

        Using md5 As MD5 = MD5.Create()
            Dim hash = md5.ComputeHash(Encoding.UTF8.GetBytes(raw))
            Dim sign = BitConverter.ToString(hash).Replace("-", "").ToUpper()
            Console.WriteLine("Signature : " & sign)
            Return sign
        End Using
    End Function

    Private Shared Async Function CallApi(commonParams As SortedDictionary(Of String, String), requestObj As Object) As Task(Of JObject)
        Dim sign = GenerateSign(commonParams, Secret)

        Dim body As New Dictionary(Of String, Object)
        For Each p In commonParams
            body(p.Key) = p.Value
        Next
        body("sign") = sign
        If requestObj IsNot Nothing Then
            body("request") = requestObj
        End If

        Dim json = JsonConvert.SerializeObject(body)
        Console.WriteLine("POST " & ApiUrl)
        Console.WriteLine("Body: " & json)

        Using client As New HttpClient()
            Dim content = New StringContent(json, Encoding.UTF8, "application/json")
            Dim response = Await client.PostAsync(ApiUrl, content)
            Dim raw = Await response.Content.ReadAsStringAsync()
            Console.WriteLine("Response: " & raw)
            Return JObject.Parse(raw)
        End Using
    End Function

    Public Shared Function GetAuthorizationUrl() As String
        Dim redirect = ConfigurationManager.AppSettings("RedirectUrl")
        Dim url = "https://seller-eu.temu.com/oauth/authorize" &
                  "?response_type=code" &
                  "&app_key=" & AppKey &
                  "&redirect_url=" & Uri.EscapeDataString(redirect)
        Console.WriteLine("Auth URL: " & url)
        Return url
    End Function

End Class