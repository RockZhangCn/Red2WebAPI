using System.Net.WebSockets;


namespace Red2WebAPI.Communication
{
    public class PlayingWebSockets {

        static Dictionary<int, WebSocket?[]> PlayingSockets = new Dictionary<int, WebSocket?[]>();
        private static readonly object _lock = new object(); // Lock object

        // for debug.
        public static int Count = 0;
        public static void AddSocket(int tableIdx, int pos, WebSocket webSocket) {
            lock (_lock) {
                if (!PlayingSockets.ContainsKey(tableIdx)) {
                    PlayingSockets[tableIdx] = new WebSocket?[4]; // Define an array of size 4
                    Console.WriteLine($"Created new socket array for table {tableIdx}");
                }
                Count++;
                PlayingSockets[tableIdx][pos-1] = webSocket; // Directly access index pos
                Console.WriteLine($"Added socket for table {tableIdx} at position {pos}");
            }
        }

        public static void RemoveSocket(int tableIdx, int pos) {
            lock (_lock) {
                if (PlayingSockets.TryGetValue(tableIdx, out var socketList)) {
                    socketList[pos-1] = null;
                    Console.WriteLine($"Removed socket for table {tableIdx} at position {pos}");
                    Count--;
                }
            }
        }

        public static WebSocket?[]? GetTableWebSockets(int tableIdx) {
            lock (_lock) {
                if (PlayingSockets.TryGetValue(tableIdx, out var socketList)) {
                    Console.WriteLine($"Retrieved sockets for table {tableIdx}");
                    return socketList; // Return the socketList if it exists
                }
                Console.WriteLine($"No sockets found for table {tableIdx}");
                return null; // Return null if the tableIdx does not exist
            }
        }

        public static int GetWebsocketPostion(int tableId, WebSocket websocket) {
            lock (_lock) {
                if (PlayingSockets.TryGetValue(tableId, out var socketList)) {
                    for (int i = 0; i < socketList.Length; i++) {
                        if (socketList[i] != websocket) {
                            return i+1;
                        }
                    }
                }
            }

            return -1;
        }

    }
}