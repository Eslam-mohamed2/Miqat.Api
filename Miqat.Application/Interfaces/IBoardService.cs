using Miqat.Application.Modules;

namespace Miqat.Application.Interfaces
{
    public interface IBoardService
    {
        Task<IEnumerable<BoardDto>> GetMineAsync(string? kind);
        /// <summary>The caller's most recently updated board of a kind, or null.</summary>
        Task<BoardDto?> GetLatestAsync(string kind);
        Task<BoardDto?> GetByIdAsync(Guid id);
        Task<BoardDto> CreateAsync(SaveBoardDto dto);
        Task<BoardDto?> UpdateAsync(Guid id, SaveBoardDto dto);
        Task<bool> DeleteAsync(Guid id);
    }
}
