using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WebApplication1.DTOs;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController(IAppointmentsService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? patientLastName,
        CancellationToken ct)
    {
        var appointments = await service.GetAllAsync(status, patientLastName, ct);
        return Ok(appointments);
    }

    [HttpPost]
    public async Task<IActionResult> Add(CreateAppointmentDto dto, CancellationToken ct)
    {
        try
        {
            var result = await service.AddAsync(dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.IdAppointment }, result);
        }
        catch (Exception e) when (e.Message.Contains("Conflict"))
        {
            return Conflict(new { message = e.Message });
        }
        catch (Exception e)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Remove(int id, CancellationToken ct)
    {
        try
        {
            await service.RemoveAsync(id, ct);
            return NoContent();
        }
        catch (InvalidOperationException e)
        {
            return Conflict(new { message = e.Message });
        }
        catch (Exception e) when (e.Message.Contains("nie istnieje"))
        {
            return NotFound(new { message = e.Message });
        }
        catch (Exception e)
        {
            return BadRequest(new { message = e.Message });
        }
    }
    
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var appointment = await service.GetByIdAsync(id, ct);
        if (appointment == null) 
            return NotFound(new { message = "Wizyta nie istnieje" });
        
        return Ok(appointment);
    }
    
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateAppointmentRequestDto dto, CancellationToken ct)
    {
        try
        {
            await service.UpdateAsync(id, dto, ct);
            return Ok(new { message = "Wizyta została zaktualizowana" }); // 200
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Wizyta nie istnieje" }); // 404
        }
        catch (InvalidOperationException e)
        {
            return Conflict(new { message = e.Message }); // 409
        }
        catch (Exception e)
        {
            return BadRequest(new { message = e.Message }); // 400
        }
    }
}