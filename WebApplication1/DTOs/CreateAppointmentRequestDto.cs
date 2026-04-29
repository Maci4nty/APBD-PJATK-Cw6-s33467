namespace WebApplication1.DTOs;

public class CreateAppointmentDto
{
    public DateTime AppointmentDate { get; set; }

    public string Status { get; set; } = "Schedulde";

    public string Reason { get; set; } = string.Empty;
    
    public int IdPatient { get; set; }
    
    public int IdDoctor { get; set; }
}