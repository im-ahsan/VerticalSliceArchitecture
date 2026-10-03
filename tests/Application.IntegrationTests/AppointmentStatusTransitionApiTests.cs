using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using VerticalSliceArchitecture.Application.Scheduling;

namespace VerticalSliceArchitecture.Application.IntegrationTests;

public class AppointmentStatusTransitionApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DoctorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly string[] ExistingResponseCodes = ["200", "400", "404"];
    private static readonly string[] ResponseCodesWithConflict = ["200", "400", "404", "409"];
    private static readonly string[] ProblemMessageProperties = ["detail", "title", "message"];

    // Each booked appointment gets its own day so bookings never overlap within the shared database.
    private static int _dayOffset = 10;

    private readonly HttpClient _client;

    public AppointmentStatusTransitionApiTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AC_1_2_CancelCompletedAppointmentReturnsConflict()
    {
        // Arrange
        var appointmentId = await BookAppointmentAsync();
        (await CompleteAsync(appointmentId, "Visit notes")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var response = await CancelAsync(appointmentId, "Patient request");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemMessagesAsync(response)).Should().Contain("Cannot cancel a completed appointment");
    }

    [Fact]
    public async Task AC_2_2_CompleteCancelledAppointmentReturnsConflict()
    {
        // Arrange
        var appointmentId = await BookAppointmentAsync();
        (await CancelAsync(appointmentId, "Patient request")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var response = await CompleteAsync(appointmentId, "Visit notes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemMessagesAsync(response)).Should().Contain("Cannot complete a cancelled appointment");
    }

    [Fact]
    public async Task AC_3_1_OpenApiListsConflictForCancelAndComplete()
    {
        // Act
        var cancelResponses = await GetDocumentedResponseCodesAsync("/api/appointments/{appointmentId}/cancel");
        var completeResponses = await GetDocumentedResponseCodesAsync("/api/appointments/{appointmentId}/complete");

        // Assert
        cancelResponses.Should().Contain(ResponseCodesWithConflict);
        completeResponses.Should().Contain(ResponseCodesWithConflict);
    }

    [Fact]
    public async Task AC_4_4_CancelWithEmptyReasonReturnsBadRequestInAnyStatus()
    {
        // Arrange
        var scheduledId = await BookAppointmentAsync();

        var completedId = await BookAppointmentAsync();
        (await CompleteAsync(completedId, null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var cancelledId = await BookAppointmentAsync();
        (await CancelAsync(cancelledId, "Patient request")).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var appointmentId in new[] { scheduledId, completedId, cancelledId })
        {
            // Act
            var response = await CancelAsync(appointmentId, string.Empty);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task CHAR_CancelScheduledAppointmentReturnsOk()
    {
        // Arrange
        var appointmentId = await BookAppointmentAsync();

        // Act
        var response = await CancelAsync(appointmentId, "Patient request");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("id").GetGuid().Should().Be(appointmentId);
        body.RootElement.GetProperty("cancellationReason").GetString().Should().Be("Patient request");
    }

    [Fact]
    public async Task CHAR_CompleteScheduledAppointmentReturnsOk()
    {
        // Arrange
        var appointmentId = await BookAppointmentAsync();

        // Act
        var response = await CompleteAsync(appointmentId, "Visit notes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("id").GetGuid().Should().Be(appointmentId);
        body.RootElement.GetProperty("notes").GetString().Should().Be("Visit notes");
    }

    [Fact]
    public async Task CHAR_CancelAlreadyCancelledAppointmentReturnsOk()
    {
        // Arrange
        var appointmentId = await BookAppointmentAsync();
        (await CancelAsync(appointmentId, "First reason")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var response = await CancelAsync(appointmentId, "Second reason");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CHAR_CompleteAlreadyCompletedAppointmentReturnsOk()
    {
        // Arrange
        var appointmentId = await BookAppointmentAsync();
        (await CompleteAsync(appointmentId, "First notes")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var response = await CompleteAsync(appointmentId, "Second notes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CHAR_CancelMissingAppointmentReturnsNotFound()
    {
        // Act
        var response = await CancelAsync(Guid.NewGuid(), "Patient request");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CHAR_CompleteMissingAppointmentReturnsNotFound()
    {
        // Act
        var response = await CompleteAsync(Guid.NewGuid(), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CHAR_CancelWithMismatchedRouteIdReturnsBadRequest()
    {
        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/appointments/{Guid.NewGuid()}/cancel",
            new CancelAppointment.Command(Guid.NewGuid(), "Patient request"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CHAR_CompleteWithTooLongNotesReturnsBadRequest()
    {
        // Act
        var response = await CompleteAsync(Guid.NewGuid(), new string('A', 1025));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CHAR_OpenApiListsOkBadRequestAndNotFoundForCancelAndComplete()
    {
        // Act
        var cancelResponses = await GetDocumentedResponseCodesAsync("/api/appointments/{appointmentId}/cancel");
        var completeResponses = await GetDocumentedResponseCodesAsync("/api/appointments/{appointmentId}/complete");

        // Assert
        cancelResponses.Should().Contain(ExistingResponseCodes);
        completeResponses.Should().Contain(ExistingResponseCodes);
    }

    private async Task<Guid> BookAppointmentAsync()
    {
        var start = DateTimeOffset.UtcNow.AddDays(Interlocked.Increment(ref _dayOffset));
        var command = new BookAppointment.Command(
            PatientId: PatientId,
            DoctorId: DoctorId,
            Start: start,
            End: start.AddMinutes(30),
            Notes: null);

        var response = await _client.PostAsJsonAsync("/api/appointments", command);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<BookAppointment.Result>();
        return result!.Id;
    }

    private Task<HttpResponseMessage> CancelAsync(Guid appointmentId, string reason) =>
        _client.PostAsJsonAsync(
            $"/api/appointments/{appointmentId}/cancel",
            new CancelAppointment.Command(appointmentId, reason));

    private Task<HttpResponseMessage> CompleteAsync(Guid appointmentId, string? notes) =>
        _client.PostAsJsonAsync(
            $"/api/appointments/{appointmentId}/complete",
            new CompleteAppointment.Command(appointmentId, notes));

    // The problem body may carry the message in "detail" or "title"; collect whichever are present.
    private static async Task<List<string>> ReadProblemMessagesAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var messages = new List<string>();
        foreach (var property in ProblemMessageProperties)
        {
            if (body.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            {
                messages.Add(value.GetString()!);
            }
        }

        return messages;
    }

    private async Task<List<string>> GetDocumentedResponseCodesAsync(string path)
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var responses = document.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty("post")
            .GetProperty("responses");

        return responses.EnumerateObject().Select(p => p.Name).ToList();
    }
}
