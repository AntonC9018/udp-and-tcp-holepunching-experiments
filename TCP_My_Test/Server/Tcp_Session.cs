using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Protobuf.Tcp;
using Google.Protobuf;
using static Protobuf.Tcp.WithoutLobbyRequest.MessageOneofCase;
using static Protobuf.Tcp.PeerWithinLobbyRequest.MessageOneofCase;
using static Protobuf.Tcp.PeerWithinLobbyResponse.Types;
using static Protobuf.Tcp.HostWithinLobbyRequest.MessageOneofCase;
using static Protobuf.Tcp.HostWithinLobbyResponse.Types;

using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace Tcp_Test.Server
{
    public class Tcp_Session
    {
        public int id;
        public Lobby joined_lobby;
        public TcpClient client;
        public IPEndpoint private_endpoint;
        public IPEndpoint public_endpoint;
        public Tcp_State state;

        public bool IsInitalized => private_endpoint != null;


        public Tcp_Session(TcpClient client)
        {
            this.client = client;
            this.state = Tcp_State.Connecting;
        }

        public void Log(string str)
        {
            System.Console.WriteLine($"[{id}] {public_endpoint.GetAddress()}:{public_endpoint.Port} | {str}");
        }

        public void Start(Server server)
        {
            try
            {
                Initialize(server);
                server.sessions.Add(id, this);
                state = Tcp_State.WithoutLobby;
                while (state != Tcp_State.Ending && client.Connected)
                {
                    switch (state)
                    {
                        case Tcp_State.WithoutLobby:
                            ListenForWithoutLobbyRequests(server);
                            break;
                        case Tcp_State.PeerWithinLobby:
                            ListenForWithinLobbyPeerRequests(server);
                            break;
                        case Tcp_State.HostWithinLobby:
                            ListenForWithinLobbyHostRequests(server);
                            break;
                            // case Tcp_State.Ending:
                            //     // for now, just end the session 
                            //     Log("Ending session in 10 seconds, since lobby has been locked.");
                            //     Thread.Sleep(10 * 1000);
                            //     state = Tcp_State.Exiting;
                            //     break;
                    }
                }
                Log($"Ending session.");
            }
            catch (System.Exception e)
            {
                Log($"Session prematurely ended due to the exception: {e}");
            }

            server.sessions.Remove(id);
            if (joined_lobby != null)
            {
                LeaveLobby(server);
                foreach (var lobby in server.lobbies.Values)
                    server.Log(lobby.GetInfo().ToString());
            }
            if (client.Connected)
            {
                client.Close();
            }
        }

        public void Initialize(Server server)
        {
            NetworkStream stream = client.GetStream();
            public_endpoint = ((IPEndPoint)client.Client.RemoteEndPoint).Convert();

            Log($"Session initialization started.");

            var initializationRequest = InitializationRequest.Parser.ParseDelimitedFrom(stream);
            private_endpoint = initializationRequest.PrivateEndpoint;
            id = initializationRequest.ClientId;

            var initializationResponse = new InitializationResponse();
            initializationResponse.SomeLobbyIds.AddRange(server.lobbies.Keys.Take(10));
            initializationResponse.WriteDelimitedTo(stream);

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
                    while (true)
                    {
                        try
                        {
                            T message = new T();
                            Log($"...Message...");
                            message.MergeDelimitedFrom(stream);
                            return message;
                        }
                        catch
                        {
                            // I think this ultimately means `if reached end of stream`
                            // so the connection will also close if received an unexpected message.
                            // | This will close the connection if tried to parse data while 
                            // | changing states under the condition that the message has been fully
                            // | parsed when the task of listening has been canceled.
                            // Actually NOT because the 
                            if (stream.ReadByte() == -1)
                            {
                                client.Close();
                            }
                            else
                            {
                                // otherwise, skip all buffered data and keep listening
                                while (stream.ReadByte() != -1) { };
                            }
                        }
                    }
                }, listening_cancellation_token_source.Token
            );
        }

        private TaskCompletionSource<Tcp_State> change_state_task_completion_source = new TaskCompletionSource<Tcp_State>();

        public void TransitionState(Tcp_State state)
        {
            this.state = state;
            this.change_state_task_completion_source.SetResult(state);
        }

        public bool TryGetMessageOrStateChange<T>(out T result) where T : IMessage<T>, new()
        {
            Task<T> listenTask = ListenForMessage<T>();
            Task[] tasks = new Task[] { listenTask, change_state_task_completion_source.Task, null };

            const int timeout_span = 2000;

            while (true)
            {
                tasks[2] = Task.Delay(timeout_span);

                int index = Task.WaitAny(tasks);

                if (index == 1)
                {
                    // This is only used for canceling one thing -- 
                    // the listen task we have initialized right above.
                    listening_cancellation_token_source.Cancel();
                    // This might potentially be dangerous, if the state were to change too fast
                    // that is, if it were to be changed right after having been disposed of here 
                    change_state_task_completion_source.Task.Dispose();
                    change_state_task_completion_source = new TaskCompletionSource<Tcp_State>();
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
                        listening_cancellation_token_source.Cancel();
                        return false;
                    }
                    continue;
                }
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

        public void ListenForWithoutLobbyRequests(Server server)
        {
            Log($"Entered {state} state");

            // so, this happens in 2 cases:
            // 1. either a state has been changed, which would rerun the loop condition and
            //    it would exit into the main loop;
            // 2. or it were not, in which case the reading of stream failed.
            //    Either invalid data has been received or the client has diconnected.
            //    This is also checked in the while conditon.
            // UDPATE: the client.Connected property does not reflect the fact whether the
            //         client is connected. However, the stream does get closed when the connection dies down.
            if (!TryGetMessageOrStateChange(out WithoutLobbyRequest request))
            {
                Log("Unsuccessfully parsed request.");
                return;
            }

            Log("Successfully parsed request.");
            NetworkStream stream = client.GetStream();

            switch (request.MessageCase)
            {
                // this is more concise and readable, but also more susceptible to change
                // TODO: do stuff with the password
                case CreateLobbyRequest:
                    {
                        Log("Creating a new lobby...");
                        CreateLobbyResponse response = new CreateLobbyResponse();
                        if (server.TryCreateLobby(id, out Lobby lobby) && lobby.TryJoin(this))
                        {
                            joined_lobby = lobby;
                            response.LobbyId = lobby.id;
                            state = Tcp_State.HostWithinLobby;
                        }
                        response.WriteDelimitedTo(stream);
                        break;
                    }
                case JoinLobbyRequest:
                    {
                        Log($"Joining lobby {request.JoinLobbyRequest.LobbyId}...");
                        JoinLobbyResponse response = new JoinLobbyResponse();
                        if (server.lobbies.TryGetValue(request.JoinLobbyRequest.LobbyId, out Lobby lobby)
                            && lobby.TryJoin(this))
                        {
                            joined_lobby = lobby;
                            response.LobbyInfo = lobby.GetInfo();
                            state = Tcp_State.PeerWithinLobby;
                        }
                        response.WriteDelimitedTo(stream);
                        break;
                    }
                default:
                    Log($"Unexpected WithoutLobby request message. {request}");
                    break;
            }
        }


        public void ListenForWithinLobbyPeerRequests(Server server)
        {
            Log($"Entered {state} state");

            // same spiel as above goes for here as well
            if (!TryGetMessageOrStateChange(out PeerWithinLobbyRequest request))
            {
                Log("Unsuccessfully parsed request.");
                return;
            }

            Log("Successfully parsed request.");

            var outerResponse = new PeerWithinLobbyResponse();
            NetworkStream stream = client.GetStream();

            switch (request.MessageCase)
            {
                case PeerWithinLobbyRequest.MessageOneofCase.LeaveLobbyRequest:
                    {
                        LeaveLobby(server);

                        var response = new LeaveLobbyResponse();
                        response.Success = true;
                        state = Tcp_State.WithoutLobby;

                        outerResponse.LeaveLobbyResponse = response;
                        outerResponse.WriteDelimitedTo(stream);
                        break;
                    }
                default:
                    Log($"Unexpected within lobby request message.");
                    break;
            }
        }

        public void ListenForWithinLobbyHostRequests(Server server)
        {
            Log($"Entered {state} state");

            // same spiel as above goes for here as well
            if (!TryGetMessageOrStateChange(out HostWithinLobbyRequest request))
            {
                Log("Unsuccessfully parsed request.");
                return;
            }

            Log("Successfully parsed request.");

            var outerResponse = new HostWithinLobbyResponse();
            NetworkStream stream = client.GetStream();

            switch (request.MessageCase)
            {
                case HostWithinLobbyRequest.MessageOneofCase.LeaveLobbyRequest:
                    {
                        LeaveLobby(server);

                        var response = new LeaveLobbyResponse();
                        response.Success = true;
                        state = Tcp_State.WithoutLobby;

                        outerResponse.LeaveLobbyResponse = response;
                        outerResponse.WriteDelimitedTo(stream);
                        break;
                    }

                case MakeHostRequest:
                    {
                        var response = new MakeHostResponse();
                        int peer_id = request.MakeHostRequest.PeerId;

                        if (id != peer_id // since we are host
                            && joined_lobby.peers.ContainsKey(peer_id))
                        {
                            response.NewHostId = peer_id;
                            joined_lobby.peers[peer_id].BecomeHost();
                        }

                        outerResponse.MakeHostResponse = response;
                        outerResponse.WriteDelimitedTo(stream);
                        break;
                    }

                case GoRequest:
                    {
                        var host_response = new GoResponse();
                        var host_address_message = CreateAddressMessage();

                        var peer_notification = new PeerWithinLobbyResponse
                        {
                            HostAddressInfo = host_address_message
                        };

                        // TODO: run concurrently (although sending is pretty fast, I suppose)
                        foreach (var peer_id in joined_lobby.GetNonHostPeerIds())
                        {
                            var peer = joined_lobby.peers[peer_id];
                            try
                            {
                                peer.TransitionState(Tcp_State.Ending);
                                peer_notification.WriteDelimitedTo(peer.client.GetStream());
                                host_response.PeerAddressInfo.Add(peer.CreateAddressMessage());
                            }
                            catch
                            {
                                peer.Log($"An error has been catched while trying to send PeerAddressInfo");
                            }
                        }
                        state = Tcp_State.Ending;
                        outerResponse.GoResponse = host_response;
                        outerResponse.WriteDelimitedTo(stream);
                        break;
                    }
                default:
                    Log($"Unexpected within lobby request message.");
                    break;
            }
        }

        private void LeaveLobby(Server server)
        {
            Log($"Leaving lobby {joined_lobby.id}");
            joined_lobby.peers.Remove(id);
            if (joined_lobby.peers.Count == 0)
            {
                server.lobbies.Remove(id);
            }
            else if (joined_lobby.host_id == id)
            {
                Tcp_Session new_host = joined_lobby.peers.Values.First();
                new_host.BecomeHost();
            }
            joined_lobby = null;
        }

        private void BecomeHost()
        {
            TransitionState(Tcp_State.HostWithinLobby);
            joined_lobby.host_id = id;
            var stream = client.GetStream();
            var host_notification = new BecomeHostNotification();
            var response = new PeerWithinLobbyResponse
            {
                BecomeHostNotification = host_notification
            };
            response.WriteDelimitedTo(stream);

            // Maybe notify of new host
            // var peer_notification = new Pee
            // foreach (int peer_id in lobby.GetNonHostPeerIds())
            // {
            // }
        }

        public AddressInfoMessage CreateAddressMessage()
        {
            return new AddressInfoMessage
            {
                Id = id,
                PrivateEndpoint = private_endpoint,
                PublicEndpoint = public_endpoint
            };
        }
    }
}