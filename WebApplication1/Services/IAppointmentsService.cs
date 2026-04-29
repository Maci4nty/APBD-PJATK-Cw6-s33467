using WebApplication1.DTOs;

namespace WebApplication1.Services;

public interface IAppointmentsService
{
    Task<IEnumerable<AppointmentListDto>> GetAllAsync(string? status, string? patientLastName, CancellationToken ct);
    Task<AppointmentListDto> AddAsync(CreateAppointmentDto dto, CancellationToken ct);

    Task RemoveAsync(int id, CancellationToken ct);
    
    Task<AppointmentDetailsDto?> GetByIdAsync(int id, CancellationToken ct);
    
    Task UpdateAsync(int id, UpdateAppointmentRequestDto dto, CancellationToken ct);
    
}