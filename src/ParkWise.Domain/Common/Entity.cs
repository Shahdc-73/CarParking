namespace ParkWise.Domain.Common;

public abstract class Entity
{
    // Version 7 GUIDs are time-ordered, so clustered-index inserts stay mostly append-only
    // (random v4 GUIDs fragment the index). Ids are created in code, not by the database.
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}
