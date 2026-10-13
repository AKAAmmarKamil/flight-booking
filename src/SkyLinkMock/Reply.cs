namespace SkyLinkMock;

public static class ErrorCodes
{
    public const string Success = "0";
    public const string InvalidSign = "A001";
    public const string OrderNotFound = "B001";
    public const string PrepaidBalanceTooLow = "B022";
    public const string DuplicateOrder = "B029";
    public const string FareNotAvailable = "B050";
}

public static class Reply
{
    public static IResult Ok(object data) => Results.Ok(new SkyLinkResponse(ErrorCodes.Success, Data: data));

    public static IResult Error(string errorCode, string errorMsg) => Results.Ok(new SkyLinkResponse(errorCode, errorMsg));
}
