using DatadogObservability.Api.DTOs;
using DatadogObservability.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DatadogObservability.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController(UserService users) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await users.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) =>
        await users.GetAsync(id, ct) is { } user ? Ok(user) : NotFound();
}
