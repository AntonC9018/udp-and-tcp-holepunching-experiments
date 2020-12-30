Now, the problem here is the `try-catches`. 
The author of the original code intended their packet classes to be used in a little bit of a different way than those of google. They had `TryParse()` methods, which as of yet [do not exist in google's protobuf](https://github.com/protocolbuffers/protobuf/issues/4100). 

Cleaning those up will require reorganizing how the code works a little bit. The author's implementation was to have a central processing while loop, but I guess in this case a state machine would be more useful. With a state machine, it would be easier to change this simple server into a matchmaking one. In its simplest form, it would have rooms, which will be pointed to by `id` fields in the packets.

Another maybe useful thing would be to wrap all packets in another packet with `oneof` modifier, [like this](https://stackoverflow.com/a/30565011/9731532).

These are just some thoughts.