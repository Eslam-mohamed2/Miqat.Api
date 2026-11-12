using Miqat.Domain.Common;
using Miqat.Domain.Enumerations;
using System;

namespace Miqat.Domain.Entities
{
    /// <summary>
    /// A saved whiteboard or node diagram.
    /// <para>
    /// Both canvases store free-form geometry that only the client understands
    /// — strokes and notes, or nodes and edges. Modelling every stroke as a row
    /// would buy nothing: the server never queries inside a drawing, it only
    /// hands the whole thing back to the editor that wrote it. So the contents
    /// live in one JSON column and the columns beside it are the parts the
    /// server does reason about: who owns it, which canvas it is, and when it
    /// last changed.
    /// </para>
    /// </summary>
    public class Board : BaseEntity
    {
        public BoardKind Kind { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Client-owned JSON payload. Opaque to the server by design.</summary>
        public string Content { get; set; } = "{}";

        public Guid OwnerId { get; set; }
        public virtual User Owner { get; set; } = null!;

        public Board(BoardKind kind, string name, string content, Guid ownerId)
        {
            if (ownerId == Guid.Empty)
                throw new ArgumentException("OwnerId cannot be empty.", nameof(ownerId));

            Kind = kind;
            Name = string.IsNullOrWhiteSpace(name)
                ? (kind == BoardKind.NodeFlow ? "Untitled flow" : "Untitled board")
                : name.Trim();
            Content = string.IsNullOrWhiteSpace(content) ? "{}" : content;
            OwnerId = ownerId;
        }

        private Board() { }

        public void Update(string? name, string? content)
        {
            if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
            if (!string.IsNullOrWhiteSpace(content)) Content = content;
            // BaseEntity owns the timestamp; its setter is private by design.
            SetUpdated();
        }
    }
}
