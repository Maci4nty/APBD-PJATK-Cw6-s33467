namespace WebApplication1.DTOs;

public class ErrorResponseDto
{
    public string Message { get; set; } = string.Empty;
    public DateTime TimeStamp { get; set; } = DateTime.Now;
}