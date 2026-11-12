namespace Miqat.Application.Modules;

/// <summary>Step 1 of changing the sign-in address: a code is sent to the new address.</summary>
public class ChangeEmailDto
{
    public string NewEmail { get; set; } = string.Empty;
    public string CurrentPassword { get; set; } = string.Empty;
}

/// <summary>Step 2: the code that arrived at the new address.</summary>
public class ConfirmEmailChangeDto
{
    public string Code { get; set; } = string.Empty;
}
