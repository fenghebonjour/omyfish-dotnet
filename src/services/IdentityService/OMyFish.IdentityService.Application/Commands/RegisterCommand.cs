using System.Text.RegularExpressions;
using OMyFish.IdentityService.Domain.Entities;
using OMyFish.IdentityService.Domain.Interfaces;
using OMyFish.Shared.BuildingBlocks.CQRS;

namespace OMyFish.IdentityService.Application.Commands;

public sealed record RegisterCommand(string Email, string Password, string? DisplayName) : ICommand<UserDto>;

public sealed record UserDto(Guid Id, string Email, string? DisplayName, string Role);

internal sealed class RegisterCommandHandler : ICommandHandler<RegisterCommand, UserDto>
{
    private const int MinPasswordLength = 8;
    private const int MaxFieldLength = 255;
    private static readonly Regex EmailShape = new(@"^[^@\s]+@[^@\s.]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly IUserRepository _users;
    private readonly ISubscriptionRepository _subscriptions;

    public RegisterCommandHandler(IUserRepository users, ISubscriptionRepository subscriptions)
    {
        _users = users;
        _subscriptions = subscriptions;
    }

    public async Task<UserDto> Handle(RegisterCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Email) || string.IsNullOrWhiteSpace(command.Password))
            throw new ArgumentException("Email and password are required.");
        if (command.Email.Length > MaxFieldLength || !EmailShape.IsMatch(command.Email))
            throw new ArgumentException("A valid email is required.");
        if (command.Password.Length is < MinPasswordLength or > MaxFieldLength)
            throw new ArgumentException(
                $"Password must be between {MinPasswordLength} and {MaxFieldLength} characters.");
        if (command.DisplayName is { Length: > MaxFieldLength })
            throw new ArgumentException("Display name is too long.");

        if (await _users.FindByEmailAsync(command.Email, ct) is not null)
            throw new EmailAlreadyRegisteredException("Email already registered.");

        var hashed = BCrypt.Net.BCrypt.HashPassword(command.Password);
        var user = User.Create(command.Email, hashed, command.DisplayName);
        await _users.CreateAsync(user, ct);
        await _subscriptions.CreateAsync(Subscription.StartTrial(user.Id), ct);

        return new UserDto(user.Id, user.Email, user.DisplayName, user.Role);
    }
}

/// The user is asking to register an email that's already taken. ->409 at the endpoint.
public sealed class EmailAlreadyRegisteredException(string message) : Exception(message);
