using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using WebApplication1.DTOs;

namespace WebApplication1.Services;

public class AppointmentsService(IConfiguration configuration) : IAppointmentsService
{
    private string ConnectionString => configuration.GetConnectionString("Default")!;

    public async Task<IEnumerable<AppointmentListDto>> GetAllAsync(string? status, string? patientLastName,
        CancellationToken ct)
    {
        var result = new List<AppointmentListDto>();

        var sqlCommand = new StringBuilder("""
                                           SELECT
                                           a.IdAppointment,
                                           a.AppointmentDate,
                                           a.Status,
                                           a.Reason,
                                           p.FirstName + ' ' + p.LastName as PatientFullName,
                                           p.Email AS PatientEmail
                                           FROM Appointments a 
                                           JOIN Patients p ON a.IdPatient = p.IdPatient
                                           """);

        var conditions = new List<string>();
        var parameters = new List<SqlParameter>();

        if (status is not null)
        {
            conditions.Add("a.Status = @Status");
            parameters.Add(new SqlParameter("@Status", status));
        }

        if (patientLastName is not null)
        {
            conditions.Add("p.LastName = @LastName");
            parameters.Add(new SqlParameter("@LastName", patientLastName));
        }

        if (parameters.Count > 0)
        {
            sqlCommand.Append(" WHERE ");
            sqlCommand.Append(string.Join(" AND ", conditions));
        }

        sqlCommand.Append(" ORDER BY a.AppointmentDate;");

        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand(sqlCommand.ToString(), connection);
        command.Parameters.AddRange(parameters.ToArray());

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            result.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(0),
                AppointmentDate = reader.GetDateTime(1),
                Status = reader.GetString(2),
                Reason = reader.GetString(3),
                PatientFullName = reader.GetString(4),
                PatientEmail = reader.GetString(5)
            });
        }

        return result;
    }

    public async Task RemoveAsync(int id, CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);

        var check = new SqlCommand("SELECT Status FROM Appointments WHERE IdAppointment = @Id", connection);
        check.Parameters.AddWithValue("@Id", id);
        
        var status = await check.ExecuteScalarAsync(ct);
        
        if (status is null)
            throw new Exception("Wizyta nie istnieje");
        
        if (status.ToString() == "Completed")
            throw new InvalidOperationException("Nie można usunąć zakończonej wizyty.");
        
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            var delete = new SqlCommand("DELETE FROM Appointments WHERE IdAppointment = @Id", connection,
                (SqlTransaction)transaction);
            delete.Parameters.AddWithValue("@Id", id);
            await delete.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<AppointmentListDto> AddAsync(CreateAppointmentDto dto, CancellationToken ct)
    {
        if (dto.AppointmentDate < DateTime.Now) throw new Exception("Termin wizyty nie może być w przeszłości.");
        if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Length > 250) throw new Exception("Opis jest wymagany (max 250 znaków).");
        
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new SqlCommand();
        command.Connection = connection;
        command.Transaction = (SqlTransaction)transaction;
        
        var conflictCmd = new SqlCommand("SELECT 1 FROM Appointments WHERE IdDoctor = @IdDoc AND AppointmentDate = @Date", connection);
        conflictCmd.Parameters.AddWithValue("@IdDoc", dto.IdDoctor);
        conflictCmd.Parameters.AddWithValue("@Date", dto.AppointmentDate);
        if (await conflictCmd.ExecuteScalarAsync(ct) is not null) throw new Exception("Lekarz ma już zajęty ten termin (Conflict).");

        try
        {
            command.CommandText = "SELECT 1 FROM Patients WHERE IdPatient = @IdPatient";
            command.Parameters.AddWithValue("@IdPatient", dto.IdPatient);
            if (await command.ExecuteScalarAsync(ct) is null)
                throw new Exception("Pacjent nie istnieje");

            command.Parameters.Clear();
            command.CommandText = "SELECT 1 FROM Doctors WHERE IdDoctor = @IdDoctor";
            command.Parameters.AddWithValue("@IdDoctor", dto.IdDoctor);
            if (await command.ExecuteScalarAsync(ct) is null)
                throw new Exception("Lekarz nie istnieje");

            command.Parameters.Clear();
            command.CommandText = """
                                  INSERT INTO Appointments(AppointmentDate, Status, Reason, IdPatient, IdDoctor) 
                                  OUTPUT INSERTED.IdAppointment
                                  VALUES (@Date, @Status, @Reason, @IdPatient, @IdDoctor)
                                  """;

            command.Parameters.AddWithValue("@Date", dto.AppointmentDate);
            command.Parameters.AddWithValue("@Status", dto.Status);
            command.Parameters.AddWithValue("@Reason", dto.Reason);
            command.Parameters.AddWithValue("@IdPatient", dto.IdPatient);
            command.Parameters.AddWithValue("@IdDoctor", dto.IdDoctor);

            var newId = (int)await command.ExecuteScalarAsync(ct)!;

            await transaction.CommitAsync(ct);

            return new AppointmentListDto()
            {
                IdAppointment = newId,
                AppointmentDate = dto.AppointmentDate,
                Status = dto.Status,
                Reason = dto.Reason,
                PatientFullName = "Nowa wizyta"
            };
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
    
    public async Task<AppointmentDetailsDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await using var command = new SqlCommand("""
                                                 SELECT 
                                                     a.IdAppointment, a.AppointmentDate, a.Status, a.Reason, a.InternalNotes, a.CreatedAt,
                                                     p.FirstName + ' ' + p.LastName as PatientFullName, p.Email, p.PhoneNumber,
                                                     d.FirstName + ' ' + d.LastName as DoctorFullName, d.LicenseNumber
                                                 FROM Appointments a
                                                 JOIN Patients p ON a.IdPatient = p.IdPatient
                                                 JOIN Doctors d ON a.IdDoctor = d.IdDoctor
                                                 WHERE a.IdAppointment = @Id
                                                 """, connection);
    
        command.Parameters.AddWithValue("@Id", id);
        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return new AppointmentDetailsDto
            {
                IdAppointment = reader.GetInt32(0),
                AppointmentDate = reader.GetDateTime(1),
                Status = reader.GetString(2),
                Reason = reader.GetString(3),
                InternalNotes = reader.IsDBNull(4) ? null : reader.GetString(4),
                CreatedAt = reader.GetDateTime(5),
                PatientFullName = reader.GetString(6),
                PatientEmail = reader.GetString(7),
                PatientPhone = reader.IsDBNull(8) ? null : reader.GetString(8),
                DocFullName = reader.GetString(9),
                LicenseNumber = reader.GetString(10)
            };
        }

        return null;
    }
    
    public async Task UpdateAsync(int id, UpdateAppointmentRequestDto dto, CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);

        var checkCmd = new SqlCommand("SELECT Status, AppointmentDate FROM Appointments WHERE IdAppointment = @Id", connection);
        checkCmd.Parameters.AddWithValue("@Id", id);
        await using var reader = await checkCmd.ExecuteReaderAsync(ct);
        
        if (!await reader.ReadAsync(ct)) throw new KeyNotFoundException();
        var oldStatus = reader.GetString(0);
        var oldDate = reader.GetDateTime(1);
        await reader.CloseAsync();

        if (oldStatus == "Completed" && dto.AppointmentDate != oldDate) 
            throw new InvalidOperationException("Nie można edytować daty zakończonej wizyty.");

        var conflict = new SqlCommand("""
                                         SELECT 1 FROM Appointments 
                                         WHERE IdDoctor = @IdD AND AppointmentDate = @Date AND IdAppointment != @Id
                                         """, connection);
        conflict.Parameters.AddWithValue("@IdD", dto.IdDoctor);
        conflict.Parameters.AddWithValue("@Date", dto.AppointmentDate);
        conflict.Parameters.AddWithValue("@Id", id);
        if (await conflict.ExecuteScalarAsync(ct) != null) 
            throw new InvalidOperationException("Lekarz ma już zajęty ten termin.");
        
        var update = new SqlCommand("""
                                       UPDATE Appointments SET AppointmentDate = @Date, Status = @Status, Reason = @Reason, 
                                       InternalNotes = @Notes, IdPatient = @IdP, IdDoctor = @IdD WHERE IdAppointment = @Id
                                       """, connection);
        update.Parameters.AddWithValue("@Id", id);
        update.Parameters.AddWithValue("@Date", dto.AppointmentDate);
        update.Parameters.AddWithValue("@Status", dto.Status);
        update.Parameters.AddWithValue("@Reason", dto.Reason);
        update.Parameters.AddWithValue("@Notes", (object?)dto.InternalNotes ?? DBNull.Value);
        update.Parameters.AddWithValue("@IdP", dto.IdPatient);
        update.Parameters.AddWithValue("@IdD", dto.IdDoctor);

        await update.ExecuteNonQueryAsync(ct);
    }
}