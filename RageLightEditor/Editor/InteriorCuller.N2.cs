using CodeWalker.GameFiles;

namespace RageLightEditor.Editor
{
    public sealed partial class InteriorCuller
    {
        public int RoomOf(YmapEntityDef e)
        {
            if (e == null) return -1;
            return roomOf.TryGetValue(e, out int r) ? r : -1;
        }

        public bool RoomReached(int room) => visibleRooms.Contains(room);

        public int ReachedRoomCount => visibleRooms.Count;
    }
}

