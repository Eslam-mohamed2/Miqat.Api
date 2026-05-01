using FluentValidation;
using Miqat.Application.Modules;

namespace Miqat.Application.Validators
{
    public class FriendshipValidator : AbstractValidator<FriendshipDto>
    {
        public FriendshipValidator()
        {
            RuleFor(x => x.SenderId)
                .NotEmpty().WithMessage("Sender ID is required.");

            RuleFor(x => x.ReceiverId)
                .NotEmpty().WithMessage("Receiver ID is required.");

            RuleFor(x => x.Status)
                .NotEmpty().WithMessage("Status is required.");
        }
    }
}
