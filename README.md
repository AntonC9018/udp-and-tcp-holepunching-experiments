# TCP and UDP hole punching tests

This is the code I wrote (or copied and modified) during the research I did for implementing multiplayer in my game. 


# Sources I used


## Protobuffs

Useful for defining custom message formats (with automatic serialization into binary and json).
https://developers.google.com/protocol-buffers/docs/tutorials


## Hole Punching

A must read paper that actually made sense. Includes info on NAT routers, describes UDP and TCP hole punching AT LENGTH:
https://bford.info/pub/net/p2pnat/

Python code people wrote based on that paper. It's not ideal, but serves as reference.
https://github.com/dwoz/python-nat-hole-punching


## UDP

UDP hole punching explanation:
https://stackoverflow.com/a/11377330/9731532

UDP hole punching implementation:
https://stackoverflow.com/a/53215243/9731532

I also cleaned up that code a little bit, see `UDP_StackOverflow`.

My simplified implementation, see `UDP_My_Test`.


## TCP

Mostly used the aforementioned paper and the python code as reference.

How To TCP NAT Traversal. A simple technique with a not self hosted relay server is presented. Note that hosting a relay server is not a problem in my case.
https://gist.github.com/mildred/b803e48801f9cdd8a4a8

TCP hole punching wiki:
https://www.wikiwand.com/en/TCP_hole_punching