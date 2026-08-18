Public Class OrderRow
    Public Property ParentOrderSn As String
    Public Property OrderSn As String
    Public Property ParentOrderStatus As Integer
    Public Property OrderStatus As Integer
    Public Property ParentOrderTime As Long
    Public Property OrderCreateTime As Long
    Public Property ExpectShipLatestTime As Long
    Public Property LatestDeliveryTime As Long

    Public Property GoodsId As Long
    Public Property SkuId As Long
    Public Property GoodsName As String
    Public Property Spec As String
    Public Property Quantity As Integer
    Public Property OriginalOrderQuantity As Integer
    Public Property ThumbUrl As String

    Public Property ReceiptName As String
    Public Property Mobile As String
    Public Property Mail As String
    Public Property RegionName1 As String
    Public Property RegionName2 As String
    Public Property RegionName3 As String
    Public Property AddressLine1 As String
    Public Property AddressLine2 As String
    Public Property PostCode As String
    Public Property AddressLineAll As String

    Public Property PackageSn As String
    Public Property OrderPaymentType As String
    Public Property FulfillmentType As String
    Public Property ShippingMethod As Integer
End Class