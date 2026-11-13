using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;

namespace Miqat.Application.Specifications.Groups;

/// <summary>
/// A page of a project's discussion, newest first: its newest comments, or the
/// ones written before a given moment. Only comments of their own; the replies
/// to them are read separately, so a reply never arrives without the comment
/// it answers.
/// </summary>
public class GroupCommentThreadsPageSpec : BaseSpecification<GroupComment>
{
    public GroupCommentThreadsPageSpec(Guid groupId, DateTime? before, int take)
        : base(c => c.GroupId == groupId && c.ParentId == null && !c.IsDeleted
                    && (before == null || c.CreatedAt < before))
    {
        AddOrderByDescending(c => c.CreatedAt);
        ApplyPaging(0, take);
    }
}
