Imports System.Net
Imports System.Configuration

Public Class CsvParser

    Private Shared Function GetCol(headers As String(), cols As String(), name As String) As String
        Dim idx = Array.IndexOf(headers, name)
        If idx >= 0 AndAlso idx < cols.Length Then
            Return cols(idx).Trim()
        End If
        Return ""
    End Function

    Public Shared Async Function DownloadAndParse() As Task(Of List(Of CsvProduct))
        Try
            Dim FtpServer = ConfigurationManager.AppSettings("FTP-Server")
            Dim FtpUser = ConfigurationManager.AppSettings("FTP-User")
            Dim FtpPassword = ConfigurationManager.AppSettings("FTP-Password")
            Dim SockFile = ConfigurationManager.AppSettings("SockFile")

            Dim url = FtpServer & SockFile
            Console.WriteLine("CSV FTP url = [" & url & "]")
            Console.WriteLine("Downloading CSV from FTP...")

            Dim lines As New List(Of String)

            Await Task.Run(Sub()
                               Dim request As FtpWebRequest = CType(FtpWebRequest.Create(url), FtpWebRequest)
                               request.Method = WebRequestMethods.Ftp.DownloadFile
                               request.Credentials = New NetworkCredential(FtpUser, FtpPassword)
                               request.EnableSsl = False
                               request.UseBinary = False
                               request.UsePassive = True

                               Using response As FtpWebResponse = CType(request.GetResponse(), FtpWebResponse)
                                   Using reader As New IO.StreamReader(response.GetResponseStream())
                                       While Not reader.EndOfStream
                                           lines.Add(reader.ReadLine())
                                       End While
                                   End Using
                               End Using
                           End Sub)

            Console.WriteLine("Downloaded " & lines.Count & " lines from CSV.")

            If lines.Count < 2 Then
                Console.WriteLine("CSV is empty or has no data rows.")
                Return New List(Of CsvProduct)()
            End If

            Dim headers = lines(0).Split(";"c)
            Dim products As New List(Of CsvProduct)

            For i = 1 To lines.Count - 1
                If String.IsNullOrWhiteSpace(lines(i)) Then Continue For

                Dim cols = lines(i).Split(";"c)

                Dim product As New CsvProduct
                product.Sku = GetCol(headers, cols, "sku")
                product.Ean = GetCol(headers, cols, "ean")
                product.Brand = GetCol(headers, cols, "brand")
                product.Manufacturer = GetCol(headers, cols, "manufacturer")
                product.Title = GetCol(headers, cols, "title")
                product.ImageUrl1 = GetCol(headers, cols, "image_1")
                product.ImageUrl2 = GetCol(headers, cols, "image_2")
                product.ImageUrl3 = GetCol(headers, cols, "image_3")
                product.ImageUrl4 = GetCol(headers, cols, "image_4")
                product.ImageUrl5 = GetCol(headers, cols, "image_5")
                product.ImageUrl6 = GetCol(headers, cols, "image_6")
                product.ImageUrl7 = GetCol(headers, cols, "image_7")
                product.ImageUrl8 = GetCol(headers, cols, "image_8")
                product.ImageUrl9 = GetCol(headers, cols, "image_9")
                product.EnergyLabelUrl = GetCol(headers, cols, "eu_energy_efficiency_class_url")
                product.Weight = GetCol(headers, cols, "weight")
                product.WeightUnit = GetCol(headers, cols, "weight_unit")
                product.Quantity = GetCol(headers, cols, "quantity")
                product.Price = GetCol(headers, cols, "product_price_vat_inc")
                product.RetailPrice = GetCol(headers, cols, "retail_price_vat_inc")

                product.Description = GetCol(headers, cols, "description")
                If String.IsNullOrWhiteSpace(product.Description) Then
                    product.Description = GetCol(headers, cols, "product_description")
                End If
                If String.IsNullOrWhiteSpace(product.Description) Then
                    product.Description = GetCol(headers, cols, "long_description")
                End If
                If String.IsNullOrWhiteSpace(product.Description) Then
                    product.Description = product.Title
                End If

                product.GpsrOrganisationName = GetCol(headers, cols, "GPSR_organisationName")
                product.GpsrStreet = GetCol(headers, cols, "GPSR_street")
                product.GpsrStreetNumber = GetCol(headers, cols, "GPSR_streetNumber")
                product.GpsrPostalCode = GetCol(headers, cols, "GPSR_postalCode")
                product.GpsrCity = GetCol(headers, cols, "GPSR_city")
                product.GpsrCountry = GetCol(headers, cols, "GPSR_country")
                product.GpsrAddressBloc = GetCol(headers, cols, "GPSR_addressBloc")
                product.GpsrPhone = GetCol(headers, cols, "GPSR_phone")
                product.GpsrEmail = GetCol(headers, cols, "GPSR_email")
                product.GpsrWebSiteURL = GetCol(headers, cols, "GPSR_webSiteURL")
                product.GpsrRegistrantNature = GetCol(headers, cols, "GPSR_registrantNature")

                products.Add(product)
            Next

            Console.WriteLine("Parsed " & products.Count & " products.")
            Return products

        Catch ex As Exception
            Console.WriteLine("CSV Parse Error: " & ex.Message)
            Return New List(Of CsvProduct)()
        End Try
    End Function

End Class