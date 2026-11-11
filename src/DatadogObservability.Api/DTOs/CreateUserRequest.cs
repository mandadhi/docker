using System.ComponentModel.DataAnnotations;

namespace DatadogObservability.Api.DTOs;

public class CreateUserRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = string.Empty;
}
