<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Form1
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        components = New ComponentModel.Container()
        Button1 = New Button()
        Button2 = New Button()
        btnEnergyBatch = New Button()
        Button3 = New Button()
        ErpelBtn = New Button()
        GpsrBtn = New Button()
        ProductIntegrationTimer = New Timer(components)
        FrBtn = New Button()
        SuspendLayout()
        ' 
        ' Button1
        ' 
        Button1.Location = New Point(276, 40)
        Button1.Name = "Button1"
        Button1.Size = New Size(230, 86)
        Button1.TabIndex = 0
        Button1.Text = "Product Integration"
        Button1.UseVisualStyleBackColor = True
        ' 
        ' Button2
        ' 
        Button2.Location = New Point(568, 60)
        Button2.Name = "Button2"
        Button2.Size = New Size(192, 46)
        Button2.TabIndex = 1
        Button2.Text = "Test"
        Button2.UseVisualStyleBackColor = True
        ' 
        ' btnEnergyBatch
        ' 
        btnEnergyBatch.Location = New Point(19, 42)
        btnEnergyBatch.Name = "btnEnergyBatch"
        btnEnergyBatch.Size = New Size(210, 84)
        btnEnergyBatch.TabIndex = 2
        btnEnergyBatch.Text = "ENERGYLABEL"
        btnEnergyBatch.UseVisualStyleBackColor = True
        ' 
        ' Button3
        ' 
        Button3.Location = New Point(568, 167)
        Button3.Name = "Button3"
        Button3.Size = New Size(192, 50)
        Button3.TabIndex = 3
        Button3.Text = "Tracking Numbers"
        Button3.UseVisualStyleBackColor = True
        ' 
        ' ErpelBtn
        ' 
        ErpelBtn.Location = New Point(19, 158)
        ErpelBtn.Name = "ErpelBtn"
        ErpelBtn.Size = New Size(210, 89)
        ErpelBtn.TabIndex = 4
        ErpelBtn.Text = "EreplButton"
        ErpelBtn.UseVisualStyleBackColor = True
        ' 
        ' GpsrBtn
        ' 
        GpsrBtn.Location = New Point(276, 180)
        GpsrBtn.Name = "GpsrBtn"
        GpsrBtn.Size = New Size(230, 67)
        GpsrBtn.TabIndex = 5
        GpsrBtn.Text = "GpsrBtn"
        GpsrBtn.UseVisualStyleBackColor = True
        ' 
        ' ProductIntegrationTimer
        ' 
        ' 
        ' FrBtn
        ' 
        FrBtn.Location = New Point(300, 312)
        FrBtn.Name = "FrBtn"
        FrBtn.Size = New Size(149, 84)
        FrBtn.TabIndex = 6
        FrBtn.Text = "FRAuth Tester"
        FrBtn.UseVisualStyleBackColor = True
        ' 
        ' Form1
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(800, 450)
        Controls.Add(FrBtn)
        Controls.Add(GpsrBtn)
        Controls.Add(ErpelBtn)
        Controls.Add(Button3)
        Controls.Add(btnEnergyBatch)
        Controls.Add(Button2)
        Controls.Add(Button1)
        Name = "Form1"
        Text = "Form1"
        ResumeLayout(False)
    End Sub

    Friend WithEvents Button1 As Button
    Friend WithEvents Button2 As Button
    Friend WithEvents btnEnergyBatch As Button
    Friend WithEvents Button3 As Button
    Friend WithEvents ErpelBtn As Button
    Friend WithEvents GpsrBtn As Button
    Friend WithEvents ProductIntegrationTimer As Timer
    Friend WithEvents FrBtn As Button

End Class
