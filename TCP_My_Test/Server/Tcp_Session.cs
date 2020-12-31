using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NetProtobuf;
using Google.Protobuf;
using static NetProtobuf.Tcp_WithoutRoomRequest.MessageOneofCase;
using static NetProtobuf.Tcp_WithinRoomRequest.MessageOneofCase;
using static NetProtobuf.Tcp_WithinRoomResponse.Types;
using System.Threading;
using System.Threading.Tasks;

namespace Tcp_Test.Server
{
    public class Tcp_Session
    {
        public int id;
        public TcpClient client;
        public IPv4_Endpoint private_endpoint;
        public IPv4_Endpoint public_endpoint;
        public State state;

        public enum State
        {
            Initializing, WithoutRoom, WithinRoom, Exiting, WithinLockedRoom
        }

        public bool IsInitalized => private_endpoint != null;
        public bool CanBeUsed => IsInitalized && client.Connected;


        public Tcp_Session(int id, TcpClient client)
        {
            this.id = id;
            this.client = client;
            this.state = State.Initializing;
        }

        public void Log(string str)
        {
            System.Console.WriteLine($"[{id}] {new IPAddress(public_endpoint.Address)}:{public_endpoint.Port} | {str}");
        }

        public void Start(Server server)
        {
            try
            {
                Initialize();
                server.sessions.Add(id, this);
                state = State.WithoutRoom;
                while (state != State.Exiting || client.Connected == false)
                {
                    switch (state)
                    {
                        case State.WithoutRoom:
                            ListenForWithoutRoomRequests(server);
                            break;
                        case State.WithinRoom:
                            ListenForWithinRoomRequests(server);
                            break;
                        case State.WithinLockedRoom:
                            // for now, just end the session 
                            Log("Ending session in 10 seconds, since lobby has been locked.");
                            Thread.Sleep(10 * 1000);
                            state = State.Exiting;
                            break;
                    }
                }
            }
            catch (System.Exception e)
            {
                Log($"Session prematurely ended due to the exception: {e}");
            }

            server.sessions.Remove(id);
            if (client.Connected)
            {
                client.Close();
            }
        }

        public void Initialize()
        {
            NetworkStream stream = client.GetStream();
            public_endpoint = ((IPEndPoint)client.Client.RemoteEndPoint).Convert();

            Log($"Session initialization started.");

            var infoMessage = InfoMessage.Parser.ParseDelimitedFrom(stream);
            private_endpoint = infoMessage.LocalEndpoint;

            stream.Write(System.BitConverter.GetBytes(id), 0, 4);

            Log($"Session established. Peer's private endpoint: {private_endpoint}");
        }

        public CancellationTokenSource listening_cancellation_token_source;

        public Task<T> ListenForMessage<T>() where T : IMessage<T>, new()
        {
            if (listening_cancellation_token_source != null)
            {
                listening_cancellation_token_source.Dispose();
            }
            listening_cancellation_token_source = new CancellationTokenSource();

            return Task.Run<T>(() =>
                {
                    NetworkStream stream = client.GetStream();
                    T message = new T();
                    message.MergeDelimitedFrom(stream);
                    return message;
                }, listening_cancellation_token_source.Token
            );
        }

        private TaskCompletionSource<State> change_state_task_completion_source = new TaskCompletionSource<State>();

        public void ChangeState(State state)
        {
            this.state = state;
            this.change_state_task_completion_source.SetResult(state);
        }

        public bool TryGetMessageOrStateChange<T>(out T result) where T : IMessage<T>, new()
        {
            Task<T> listenTask = ListenForMessage<T>();

            Task[] tasks = new Task[] { listenTask, change_state_task_completion_source.Task, null };

            while (true)
            {
                tasks[2] = Task.Delay(5000);

                int index = Task.WaitAny(tasks);

                if (index == 1)
                {
                    // This is only used for canceling one thing -- 
                    // the listen task we have initialized right above.
                    listening_cancellation_token_source.Cancel();
                    // This might potentially be dangerous, if the state were to change too fast
                    // that is, if it were to be changed right after having been disposed of here 
                    change_state_task_completion_source.Task.Dispose();
                    change_state_task_completion_source = new TaskCompletionSource<State>();
                    result = default(T);
                    return false;
                }
                // Check if the connection closes here. If it does, stop parsing.
                // We need this since the stream does not close once the connection is closed. 
                else if (index == 2)
                {
                    Log("Timeout reached while parsing data...");
                    tasks[2].Dispose();
                    if (!client.Connected)
                    {
                        Log("Client disconnected while trying to parse data.");
                        result = default(T);
                        listenTask.Dispose();
                        return false;
                    }
                    continue;
                }
                Log($"Index is {index}");

                // the listen task has terminated
                {
                    // If this threw then the client has probably disconnected, or the stream data
                    // were invalid, so no result acquired in this case.
                    if (listenTask.IsFaulted)
                    {
                        result = default(T);
                        return false;
                    }

                    // Otherwise, we read the next packet successfully.
                    result = listenTask.Result;
                    listenTask.Dispose();
                    return true;
                }
            }
        }

        public void ListenForWithoutRoomRequests(Server server)
        {
            Log($"Entered {state} state");

            NetworkStream stream = client.GetStream();
            Tcp_WithoutRoomResponse response = new Tcp_WithoutRoomResponse();

            while (state == State.WithoutRoom && client.Connected)
            {
                // so, this happens in 2 cases:
                // 1. either a state has been changed, which would rerun the loop condition and
                //    it would exit into the main loop;
                // 2. or it were not, in which case the reading of stream failed.
                //    Either invalid data has been received or the client has diconnected.
                //    This is also checked in the while conditon.
                if (!TryGetMessageOrStateChange(out Tcp_WithoutRoomRequest request))
                {
                    Log("Unsuccessfully parsed request.");
                    continue;
                }

                Log("Successfully parsed request.");

                switch (request.MessageCase)
                {
                    case Ping:
                        Log($"Pinging with 0x{request.Ping.Data:X}");
                        continue;
                    // this is more concise and readable, but also more susceptible to change
                    // TODO: do stuff with the password
                    case CreateRoomRequest:
                        var lobby = new Lobby(id);
                        response.Success = server.lobbies.TryAdd(id, lobby) && lobby.TryJoin(this);
                        Log("Creating a new lobby...");
                        break;
                    case JoinRoomRequest:
                        response.Success = server.lobbies.TryGetValue(id, out lobby) && lobby.TryJoin(this);
                        Log($"Joining lobby {id}...");
                        break;
                    default:
                        Log($"Unexpected WithoutRoom request message.");
                        break;
                }
                response.WriteDelimitedTo(stream);
                if (response.Success)
                {
                    state = State.WithinRoom;
                }
            }
        }


        public void ListenForWithinRoomRequests(Server server)
        {
            Log($"Entered {state} state");
            NetworkStream stream = client.GetStream();

            while (state == State.WithinRoom && client.Connected)
            {
                // same spiel as above goes for here as well
                if (!TryGetMessageOrStateChange(out Tcp_WithinRoomRequest request))
                {
                    continue;
                }

                switch (request.MessageCase)
                {
                    case LeaveRequest:
                        if (server.lobbies.TryGetValue(id, out Lobby lobby))
                        {
                            lobby.peers.Remove(id);
                            if (lobby.peers.Count == 0)
                            {
                                server.lobbies.Remove(id);
                            }
                            var ack = new LeaveRoomAck();
                            ack.WriteDelimitedTo(stream);
                            state = State.WithoutRoom;
                        }
                        break;
                    case StartRequest:
                        if (server.lobbies.TryGetValue(id, out lobby) && id == lobby.host_id)
                        {
                            var host_response = new StartRoomResponseHost();

                            var host_address_message = CreateAddressMessage();

                            // TODO: this should be in a separate thing
                            // Also, stop listening on socket and change state for that session on server
                            var peer_notification = new StartRoomResponsePeer
                            {
                                Host = host_address_message
                            };

                            var response = new Tcp_WithinRoomResponse
                            {
                                StartRoomResponePeer = peer_notification // whatever
                            };

                            // TODO: run concurrently (although sending is pretty fast, I suppose)
                            foreach (var peer_id in lobby.peers.Keys)
                            {
                                if (peer_id != id)
                                {
                                    var peer = lobby.peers[peer_id];
                                    try
                                    {
                                        peer.ChangeState(State.WithinLockedRoom);
                                        peer_notification.WriteDelimitedTo(peer.client.GetStream());
                                        host_response.Peers.Add(peer.CreateAddressMessage());
                                    }
                                    catch
                                    {
                                        peer.Log($"An error has been catched while trying to send PeerInfo");
                                    }
                                }
                            }
                            ChangeState(State.WithinLockedRoom);
                            host_response.WriteDelimitedTo(stream);
                            // TODO: delete or lock the lobby
                        }
                        break;
                    default:
                        Log($"Unexpected within lobby request message.");
                        break;
                }
            }
        }

        public PeerAddressMessage CreateAddressMessage()
        {
            return new PeerAddressMessage
            {
                Id = id,
                LocalEndpoint = private_endpoint,
                PublicEndpoint = public_endpoint
            };
        }
    }
}