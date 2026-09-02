Imports System.IO
Imports System.Reflection

Public Class AppState

    Private Shared Function GetTogglePath() As String
        Dim dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
        Return Path.Combine(dir, "order_tracking_enabled.txt")
    End Function

    Public Shared Function GetOrderTrackingEnabled() As Boolean
        Dim path = GetTogglePath()
        If Not File.Exists(path) Then Return True
        Dim raw = File.ReadAllText(path).Trim()
        If raw = "0" Then Return False
        Return True
    End Function

    Public Shared Sub SetOrderTrackingEnabled(enabled As Boolean)
        Dim path = GetTogglePath()
        File.WriteAllText(path, If(enabled, "1", "0"))
    End Sub

End Class