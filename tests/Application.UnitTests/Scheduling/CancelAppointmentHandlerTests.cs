using Microsoft.EntityFrameworkCore;

using VerticalSliceArchitecture.Application.Common.Interfaces;
using VerticalSliceArchitecture.Application.Domain;
using VerticalSliceArchitecture.Application.Infrastructure.Persistence;
using VerticalSliceArchitecture.Application.Scheduling;

namespace VerticalSliceArchitecture.Application.UnitTests.Scheduling;

public class CancelAppointmentHandlerTests
{
    private readonly ApplicationDbContext _context = new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"CancelAppointmentHandlerTests-{Guid.NewGuid():N}")
            .Options,
        Substitute.For<ICurrentUserService>(),
        Substitute.For<IDomainEventService>(),
        Substitute.For<IDateTime>());

    [Fact]
    public async Task AC_1_1_ReturnsConflictWhenAppointmentCompleted()
    {
        // Arrange
        var appointment = await AddAppointmentAsync(a => a.Complete("Done"));
        var handler = new CancelAppointment.Handler(_context);

        // Act
        var result = await handler.Handle(
            new CancelAppointment.Command(appointment.Id, "Patient request"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        var error = result.Errors.Should().ContainSingle().Subject;
        error.Type.Should().Be(ErrorType.Conflict);
        error.Code.Should().Be("Appointment.CannotCancel");
        error.Description.Should().Be("Cannot cancel a completed appointment");
    }

    [Fact]
    public async Task CHAR_CancelHandler_CancelsScheduledAppointment()
    {
        // Arrange
        var appointment = await AddAppointmentAsync();
        var handler = new CancelAppointment.Handler(_context);

        // Act
        var result = await handler.Handle(
            new CancelAppointment.Command(appointment.Id, "Patient request"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Id.Should().Be(appointment.Id);
        result.Value.Status.Should().Be(AppointmentStatus.Cancelled);
        result.Value.CancellationReason.Should().Be("Patient request");
    }

    [Fact]
    public async Task CHAR_CancelHandler_AlreadyCancelledAppointmentIsIdempotent()
    {
        // Arrange
        var appointment = await AddAppointmentAsync(a => a.Cancel("First reason"));
        var handler = new CancelAppointment.Handler(_context);

        // Act
        var result = await handler.Handle(
            new CancelAppointment.Command(appointment.Id, "Second reason"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Status.Should().Be(AppointmentStatus.Cancelled);
        result.Value.CancellationReason.Should().Be("First reason");
    }

    [Fact]
    public async Task CHAR_CancelHandler_ReturnsNotFoundWhenAppointmentMissing()
    {
        // Arrange
        var handler = new CancelAppointment.Handler(_context);
        var appointmentId = Guid.NewGuid();

        // Act
        var result = await handler.Handle(
            new CancelAppointment.Command(appointmentId, "Patient request"),
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
