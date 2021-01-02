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
        public IPEndPointMessage private_endpoint;
        public IPEndPointMessage public_endpoint;
        public Tcp_State state;
        public Server server;

        public bool IsInitalized => private_endpoint != null;


        public Tcp_Session(TcpClient client, Server server)
        {
            this.client = client;
            this.state = Tcp_State.Connecting;
            this.server = server;
        }

        public void Log(string str)
        {
            System.Console.WriteLine($"[{id}] {public_endpoint.GetAddress()}:{public_endpoint.Port} | {str}");
        }

        public void Start()
        {
            try
            {
                Initialize();
                server.sessions.Add(id, this);
                state = Tcp_State.WithoutLobby;
                while (state != Tcp_State.Closed && client.Connected)
                {
                    switch (state)
                    {
                        case Tcp_State.WithoutLobby:
                            ReceiveMessageAndRespond(ReceiveWithoutLobbyRequest);
                            break;
                        case Tcp_State.PeerWithinLobby:
                            ReceiveMessageAndRespond(ReceiveWithinLobbyPeerRequest);
                            break;
                        case Tcp_State.HostWithinLobby:
                            ReceiveMessageAndRespond(ReceiveWithinLobbyHostRequest);
                            break;
                        case Tcp_State.Closing:
                            // for now, just end the session 
                            Log("Closing session in 10 seconds, since lobby has been locked.");
                            Thread.Sleep(10 * 1000);
                            state = Tcp_State.Closed;
                            break;
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
                LeaveLobby();
                foreach (var lobby in server.lobbies.Values)
                    server.Log(lobby.GetInfo().ToString());
            }
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

            var initializationRequest = InitializationRequest.Parser.ParseDelimitedFrom(stream);
            private_endpoint = initializationRequest.PrivateEndpoint;
            id = initializationRequest.ClientId;

            var initializationResponse = new InitializationResponse();
            initializationResponse.SomeLobbyIds.AddRange(server.lobbies.Keys.Take(10));
            initializationResponse.WriteDelimitedTo(stream);

            Log($"Session established. Peer's private endpoint: {private_endpoint}");
        }

        public CancellationTokenSource listening_cancellation_token_source;

        /* 
            Start trying to read the network stream, trying to convert the bytes
            to the specified in the generic message type. 

            If for some reason the message could not be parsed because we received unexpected data, 
            the stream would be flushed (all buffered bytes would be discarded) and then we would
            go on trying to parse the data after that into the specified message type.

            If the task gets cancelled using the cancellation token, 
            I'm pretty sure that the catch() inside the while loop will not get executed.
            I'm also pretty sure that it is not possible for there to be data in the stream
            when we terminate the task this way.

            If all goes well, the task terminates normally with the parsed message.
        */
        public Task<T> ReceiveMessage<T>() where T : IMessage<T>, new()
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
                            message.MergeDelimitedFrom(stream);
                            return message;
                        }
                        catch
                        {
                            // This condition will execute if the client close the connection.
                            // Note that the client.Connected property does not reflect this fact.
                            if (stream.ReadByte() == -1)
                            {
                                client.Close();
                            }
                            else
                            {
                                // otherwise, skip all buffered data and keep trying to parse
                                // thus we basically ignore unexpected messages.
                                while (stream.ReadByte() != -1) { };
                            }
                        }
                    }
                }, listening_cancellation_token_source.Token
            );
        }

        private TaskCompletionSource<Tcp_State> change_state_task_completion_source = new TaskCompletionSource<Tcp_State>();

        /*
            This method is useful when changing the state of some other session from without
            its thread, so e.g. if thread of session with id = 1 transitions thread with id = 2
            into another state.

            This potentially might be dangerous to do while the other thread is in the process
            of receiving messages, which is why:
                a. the listen task gets cancelled to not misinterpret the message.
                b. if in the middle of parsing, the message and the buffered data is all discarded.
            
            An ideal solution would be to finish parsing, if in the middle of parsing, but then discard 
            the message, however, I have no way of knowing that information, since I'm using protobuf for packets.  
        */
        public void TransitionState(Tcp_State state)
        {
            this.state = state;
            this.change_state_task_completion_source.SetResult(state);
        }

        /*
            This listens for one of 2 tasks at the same time:
                1. the listen task, which parses data received from the client.
                2. the change state task, which is responsible for quitting listening for message
                   if the message type we're trying to parse to is not correct.

        */
        public bool TryGetMessageOrStateChange<T>(out T result) where T : IMessage<T>, new()
        {
            Task<T> receiveTask = ReceiveMessage<T>();
            Task[] tasks = new Task[] { receiveTask, change_state_task_completion_source.Task };

            while (true)
            {
                int index = Task.WaitAny(tasks);

                if (index == 1)
                {
                    // This is only used for canceling one thing -- the listen task we have initialized right above.
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
                if (!client.Connected)
                {
                    Log("Client disconnected while trying to parse data.");
                    result = default(T);
                    listening_cancellation_token_source.Cancel();
                    return false;
                }
                // The listen task has terminated.
                // If this threw then the client has probably disconnected but this is already being detected above,
                // or the stream data were invalid, so no result acquired in this case. If you think about this,
                // this option is never reached, since if any of the above happers, then the client has been disconnected.
                if (receiveTask.IsFaulted)
                {
                    result = default(T);
                    return false;
                }

                // Otherwise, we read the next packet successfully.
                result = receiveTask.Result;
                receiveTask.Dispose();
                return true;
            }
        }


        public void ReceiveMessageAndRespond(Func<IMessage> receiveFunc)
        {
            var response = receiveFunc();
            if (response != null)
            {
                NetworkStream stream = client.GetStream();
                response.WriteDelimitedTo(stream);
            }
        }

        public IMessage ReceiveWithoutLobbyRequest()
        {
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
                return null;
            }

            switch (request.MessageCase)
            {
                // TODO: do stuff with the password
                case CreateLobbyRequest:
                    {
                        Log("Creating a new lobby...");
                        CreateLobbyResponse response = new CreateLobbyResponse();
                        if (server.TryCreateLobby(id, out Lobby lobby) && lobby.TryJoin(this))
                        {
                            Log($"Creating a new lobby {lobby.id}");
                            joined_lobby = lobby;
                            response.LobbyId = lobby.id;
                            state = Tcp_State.HostWithinLobby;
                        }
                        return response;
                    }

                case JoinLobbyRequest:
                    {
                        Log($"Joining lobby {request.JoinLobbyRequest.LobbyId}...");
                        JoinLobbyResponse response = new JoinLobbyResponse();
                        if (server.lobbies.TryGetValue(request.JoinLobbyRequest.LobbyId, out Lobby lobby)
                            && lobby.TryJoin(this))
                        {
                            Log($"Joined lobby {request.JoinLobbyRequest.LobbyId}.");
                            joined_lobby = lobby;
                            response.LobbyInfo = lobby.GetInfo();
                            state = Tcp_State.PeerWithinLobby;
                        }
                        return response;
                    }

                case MyAddressInfoRequest:
                    {
                        Log($"Sending address info message.");
                        return CreateAddressMessage();
                    }

                default:
                    Log($"Unexpected WithoutLobby request message. {request}");
                    return null;
            }
        }


        public PeerWithinLobbyResponse ReceiveWithinLobbyPeerRequest()
        {
            // same spiel as above goes for here as well
            if (!TryGetMessageOrStateChange(out PeerWithinLobbyRequest request))
            {
                return null;
            }

            switch (request.MessageCase)
            {
                case PeerWithinLobbyRequest.MessageOneofCase.LeaveLobbyRequest:
                    {
                        LeaveLobby();
                        state = Tcp_State.WithoutLobby;
                        var response = new LeaveLobbyResponse();
                        response.Success = true;

                        return new PeerWithinLobbyResponse
                        {
                            LeaveLobbyResponse = response
                        };
                    }
                default:
                    Log($"Unexpected within lobby request message.");
                    return null;
            }
        }

        public HostWithinLobbyResponse ReceiveWithinLobbyHostRequest()
        {
            // same spiel as above goes for here as well
            if (!TryGetMessageOrStateChange(out HostWithinLobbyRequest request))
            {
                return null;
            }

            var outerResponse = new HostWithinLobbyResponse();

            switch (request.MessageCase)
            {
                case HostWithinLobbyRequest.MessageOneofCase.LeaveLobbyRequest:
                    {
                        LeaveLobby();

                        var response = new LeaveLobbyResponse();
                        response.Success = true;
                        state = Tcp_State.WithoutLobby;

                        return new HostWithinLobbyResponse
                        {
                            LeaveLobbyResponse = response
                        };
                    }

                case MakeHostRequest:
                    {
                        var response = new MakeHostResponse();
                        int peer_id = request.MakeHostRequest.PeerId;

                        if (id != peer_id // since we are host
                            && joined_lobby.peers.ContainsKey(peer_id))
                        {
                            response.NewHostId = peer_id;
                            MakeHost(joined_lobby.peers[peer_id]);
                        }

                        return new HostWithinLobbyResponse
                        {
                            MakeHostResponse = response
                        };
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
                                peer.joined_lobby = null;
                                peer.TransitionState(Tcp_State.Closing);
                                peer_notification.WriteDelimitedTo(peer.client.GetStream());
                                host_response.PeerAddressInfo.Add(peer.CreateAddressMessage());
                            }
                            catch
                            {
                                peer.Log($"An error has been catched while trying to send PeerAddressInfo");
                            }
                        }
                        server.lobbies.Remove(joined_lobby.id);
                        joined_lobby = null;
                        state = Tcp_State.Closing;
                        return new HostWithinLobbyResponse
                        {
                            GoResponse = host_response
                        };
                    }
                default:
                    Log($"Unexpected within lobby request message.");
                    return null;
            }
        }

        private void LeaveLobby()
        {
            // cannot leave a started lobby
            Log($"Leaving lobby {joined_lobby.id}");
            joined_lobby.peers.Remove(id);
            if (joined_lobby.peers.Count == 0)
            {
                server.lobbies.Remove(joined_lobby.id);
            }
            else if (joined_lobby.host_id == id)
            {
                Tcp_Session new_host = joined_lobby.peers.Values.First();
                new_host.BecomeHost();
            }
            joined_lobby = null;
        }

        private void MakeHost(Tcp_Session session)
        {
            TransitionState(Tcp_State.PeerWithinLobby);
            session.TransitionState(Tcp_State.HostWithinLobby);
            session.BecomeHost();
        }

        private void BecomeHost()
        {
            joined_lobby.host_id = id;
            var host_notification = new BecomeHostNotification();
            var response = new PeerWithinLobbyResponse
            {
                BecomeHostNotification = host_notification
            };
            response.WriteDelimitedTo(client.GetStream());

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