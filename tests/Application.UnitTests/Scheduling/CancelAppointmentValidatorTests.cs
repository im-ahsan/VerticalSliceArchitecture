using FluentValidation.TestHelper;

using VerticalSliceArchitecture.Application.Scheduling;

namespace VerticalSliceArchitecture.Application.UnitTests.Scheduling;

public class CancelAppointmentValidatorTests
{
    private readonly CancelAppointment.Validator _validator = new();

    [Fact]
    public void AC_4_1_FailsWhenReasonEmpty()
    {
        // Arrange
        var command = new CancelAppointment.Command(Guid.NewGuid(), string.Empty);

        // Act & Assert
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Reason)
            .WithErrorMessage("Cancellation reason is required");
    }

    [Fact]
    public void AC_4_2_FailsWhenReasonExceeds512Characters()
    {
        // Arrange
        var command = new CancelAppointment.Command(Guid.NewGuid(), new string('A', 513));

        // Act & Assert
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Reason)
            .WithErrorMessage("Cancellation reason cannot exceed 512 characters");
    }

    [Fact]
    public void CHAR_CancelValidator_AcceptsReasonOf512Characters()
    {
        // Arrange
        var command = new CancelAppointment.Command(Guid.NewGuid(), new string('A', 512));

        // Act & Assert
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CHAR_CancelValidator_FailsWhenAppointmentIdEmpty()
    {
        // Arrange
        var command = new CancelAppointment.Command(Guid.Empty, "Patient request");

        // Act & Assert
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.AppointmentId)
            .WithErrorMessage("AppointmentId is required");
    }
}
