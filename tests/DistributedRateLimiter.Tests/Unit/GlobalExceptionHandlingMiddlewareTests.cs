using System.IO;
using System.Text.Json;
using DistributedRateLimiter.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace DistributedRateLimiter.Tests.Unit;

public class GlobalExceptionHandlingMiddlewareTests
{
    private readonly Mock<ILogger<GlobalExceptionHandlingMiddleware>> _loggerMock = new();
    private readonly Mock<IHostEnvironment> _envMock = new();

    [Fact]
    public async Task InvokeAsync_WhenExceptionThrown_CatchesAndReturnsRfc7807ProblemDetails()
    {
        // Arrange
        _envMock.Setup(e => e.EnvironmentName).Returns(Environments.Production);

        RequestDelegate next = (_) => throw new InvalidOperationException("Simulated critical failure.");
        var middleware = new GlobalExceptionHandlingMiddleware(next, _loggerMock.Object, _envMock.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test-failure";
        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);

        responseStream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(responseStream);
        var responseBody = await reader.ReadToEndAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(problemDetails);
        Assert.Equal(StatusCodes.Status500InternalServerError, problemDetails.Status);
        Assert.Equal("/api/test-failure", problemDetails.Instance);
    }
}
