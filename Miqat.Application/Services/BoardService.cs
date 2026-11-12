using System.Text.Json;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <summary>
    /// Saved whiteboards and node diagrams.
    /// <para>
    /// Boards are private to their owner. There is no sharing model here yet, so
    /// rather than route through IAccessPolicy — which answers questions about
    /// projects and tasks — every read and write filters on the caller's own id.
    /// A board that is not yours simply does not exist as far as this service is
    /// concerned, which is also why the delete/update paths return null rather
    /// than throwing: nothing should reveal that someone else's id is real.
    /// </para>
    /// </summary>
    public class BoardService : IBoardService
    {
        private const int MaxNameLength = 120;          // Boards.Name column size
        private const int MaxContentLength = 2_000_000; // ~2 MB of JSON text

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;

        public BoardService(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
        }

        private static BoardKind ParseKind(string? kind) =>
            string.Equals(kind, "NodeFlow", StringComparison.OrdinalIgnoreCase)
                ? BoardKind.NodeFlow
                : BoardKind.Whiteboard;

        /// <summary>
        /// The content column is jsonb, so text that is not JSON is rejected by
        /// PostgreSQL itself — which used to surface as a 500. Checked here so the
        /// caller gets a 400 that says what is wrong.
        /// </summary>
        private static void Validate(SaveBoardDto dto)
        {
            if (dto.Name != null && dto.Name.Trim().Length > MaxNameLength)
                throw new ApiException($"Board name cannot exceed {MaxNameLength} characters.", 400);

            if (string.IsNullOrWhiteSpace(dto.Content)) return;

            if (dto.Content.Length > MaxContentLength)
                throw new ApiException("This board is too large to save.", 413);

            try
            {
                using var _ = JsonDocument.Parse(dto.Content);
            }
            catch (JsonException)
            {
                throw new ApiException("Board content must be valid JSON.", 400);
            }
        }

        public async Task<IEnumerable<BoardDto>> GetMineAsync(string? kind)
        {
            var userId = _currentUser.RequireUserId();
            var boards = await _unitOfWork.Repository<Board>()
                .FindAsync(b => b.OwnerId == userId && !b.IsDeleted);

            if (!string.IsNullOrWhiteSpace(kind))
            {
                var wanted = ParseKind(kind);
                boards = boards.Where(b => b.Kind == wanted);
            }

            return boards
                .OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt)
                .Select(MapToDto)
                .ToList();
        }

        public async Task<BoardDto?> GetLatestAsync(string kind)
        {
            var boards = await GetMineAsync(kind);
            return boards.FirstOrDefault();
        }

        public async Task<BoardDto?> GetByIdAsync(Guid id)
        {
            var userId = _currentUser.RequireUserId();
            var board = await _unitOfWork.Repository<Board>().GetByIdAsync(id);
            if (board == null || board.IsDeleted || board.OwnerId != userId) return null;
            return MapToDto(board);
        }

        public async Task<BoardDto> CreateAsync(SaveBoardDto dto)
        {
            var userId = _currentUser.RequireUserId();
            Validate(dto);

            var board = new Board(ParseKind(dto.Kind), dto.Name ?? string.Empty, dto.Content, userId);

            await _unitOfWork.Repository<Board>().AddAsync(board);
            await _unitOfWork.CompleteAsync();
            return MapToDto(board);
        }

        public async Task<BoardDto?> UpdateAsync(Guid id, SaveBoardDto dto)
        {
            var userId = _currentUser.RequireUserId();
            var board = await _unitOfWork.Repository<Board>().GetByIdAsync(id);
            if (board == null || board.IsDeleted || board.OwnerId != userId) return null;

            Validate(dto);

            board.Update(dto.Name, dto.Content);
            await _unitOfWork.CompleteAsync();
            return MapToDto(board);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var userId = _currentUser.RequireUserId();
            var board = await _unitOfWork.Repository<Board>().GetByIdAsync(id);
            if (board == null || board.IsDeleted || board.OwnerId != userId) return false;

            _unitOfWork.Repository<Board>().Delete(board);
            await _unitOfWork.CompleteAsync();
            return true;
        }

        private static BoardDto MapToDto(Board board) => new()
        {
            Id = board.Id,
            Kind = board.Kind.ToString(),
            Name = board.Name,
            Content = board.Content,
            CreatedAt = board.CreatedAt,
            UpdatedAt = board.UpdatedAt
        };
    }
}
