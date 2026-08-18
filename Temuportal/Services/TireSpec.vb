Imports System.Text.RegularExpressions

Public Class TireSpec

    Public Property WidthMm As Integer
    Public Property AspectRatio As Integer
    Public Property RimInch As Integer
    Public Property LoadIndex As Integer
    Public Property SpeedLetter As String
    Public Property Raw As String

    Public ReadOnly Property IsComplete As Boolean
        Get
            Return WidthMm > 0 AndAlso AspectRatio > 0 AndAlso RimInch > 0 _
                   AndAlso LoadIndex > 0 AndAlso Not String.IsNullOrEmpty(SpeedLetter)
        End Get
    End Property

    Public ReadOnly Property WidthInch As Double
        Get
            Return Math.Round(WidthMm / 25.4, 2)
        End Get
    End Property

    Public ReadOnly Property OverallDiameterInch As Double
        Get
            Dim sidewallMm = WidthMm * (AspectRatio / 100.0)
            Return Math.Round(RimInch + (2.0 * sidewallMm / 25.4), 2)
        End Get
    End Property

    Public Shared Function Parse(title As String) As TireSpec
        If String.IsNullOrWhiteSpace(title) Then Return Nothing

        Dim sizeMatch = Regex.Match(title, "(\d{2,3})\s*/\s*(\d{2})\s*(?:Z)?\s*R\s*(\d{2})", RegexOptions.IgnoreCase)
        If Not sizeMatch.Success Then Return Nothing

        Dim spec As New TireSpec()
        spec.WidthMm = Integer.Parse(sizeMatch.Groups(1).Value)
        spec.AspectRatio = Integer.Parse(sizeMatch.Groups(2).Value)
        spec.RimInch = Integer.Parse(sizeMatch.Groups(3).Value)
        spec.Raw = $"{spec.WidthMm}/{spec.AspectRatio} R{spec.RimInch}"

        Dim tail = title.Substring(sizeMatch.Index + sizeMatch.Length)
        Dim lsMatch = Regex.Match(tail, "^\s*(?:C\s+|XL\s+|TL\s+|TLC\s+|RF\s+|[A-Z]{1,3}\s+)*?\(?(\d{2,3})(?:/(\d{2,3}))?\)?\s*\(?([A-Z])\)?\b", RegexOptions.IgnoreCase)
        If lsMatch.Success Then
            spec.LoadIndex = Integer.Parse(lsMatch.Groups(1).Value)
            spec.SpeedLetter = lsMatch.Groups(3).Value.ToUpperInvariant()
        End If

        Return spec
    End Function

End Class