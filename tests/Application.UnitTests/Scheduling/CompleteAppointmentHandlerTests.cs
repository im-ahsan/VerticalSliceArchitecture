using Microsoft.EntityFrameworkCore;

using VerticalSliceArchitecture.Application.Common.Interfaces;
using VerticalSliceArchitecture.Application.Domain;
using VerticalSliceArchitecture.Application.Infrastructure.Persistence;
using VerticalSliceArchitecture.Application.Scheduling;

namespace VerticalSliceArchitecture.Application.UnitTests.Scheduling;

public class CompleteAppointmentHandlerTests
{
    private readonly ApplicationDbContext _context = new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"CompleteAppointmentHandlerTests-{Guid.NewGuid():N}")
            .Options,
        Substitute.For<ICurrentUserService>(),
        Substitute.For<IDomainEventService>(),
        Substitute.For<IDateTime>());

    [Fact]
    public async Task AC_2_1_ReturnsConflictWhenAppointmentCancelled()
    {
        // Arrange
        var appointment = await AddAppointmentAsync(a => a.Cancel("Patient request"));
        var handler = new CompleteAppointment.Handler(_context);

        // Act
        var result = await handler.Handle(
            new CompleteAppointment.Command(appointment.Id, "Visit notes"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        var error = result.Errors.Should().ContainSingle().Subject;
        error.Type.Should().Be(ErrorType.Conflict);
        error.Code.Should().Be("Appointment.CannotComplete");
        error.Description.Should().Be("Cannot complete a cancelled appointment");
    }

    [Fact]
    public async Task CHAR_CompleteHandler_CompletesScheduledAppointment()
    {
        // Arrange
        var appointment = await AddAppointmentAsync();
        var handler = new CompleteAppointment.Handler(_context);

        // Act
        var result = await handler.Handle(
            new CompleteAppointment.Command(appointment.Id, "Visit notes"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Id.Should().Be(appointment.Id);
        result.Value.Status.Should().Be(AppointmentStatus.Completed);
        result.Value.Notes.Should().Be("Visit notes");
    }

    [Fact]
    public async Task CHAR_CompleteHandler_AlreadyCompletedAppointmentIsIdempotent()
    {
        // Arrange
        var appointment = await AddAppointmentAsync(a => a.Complete("First notes"));
        var handler = new CompleteAppointment.Handler(_context);

        // Act
        var result = await handler.Handle(
            new CompleteAppointment.Command(appointment.Id, null),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Status.Should().Be(AppointmentStatus.Completed);
        result.Value.Notes.Should().Be("First notes");
    }

    [Fact]
    public async Task CHAR_CompleteHandler_ReturnsNotFoundWhenAppointmentMissing()
    {
        // Arrange
        var handler = new CompleteAppointment.Handler(_context);
        var appointmentId = Guid.NewGuid();

        // Act
        var result = await handler.Handle(
            new CompleteAppointment.Command(appointmentId, null),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        var error = result.Errors.Should().ContainSingle().Subject;
        error.Type.Should().Be(ErrorType.NotFound);
        error.Code.Should().Be("Appointment.NotFound");
        error.Description.Should().Be($"Appointment with ID {appointmentId} not found");
    }

    private async Task<Appointment> AddAppointmentAsync(Action<Appointment>? transition = null)
    {
        var appointment = Appointment.Schedule(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddHours(1),
            DateTime.UtcNow.AddHours(2));
        transition?.Invoke(appointment);

        _context.Appointments.Add(appointment);
        await _context.SaveChangesAsync();

        return appointment;
    }
}
