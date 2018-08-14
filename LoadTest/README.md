## Telepathy Load Test ##

Spawn clients and servers to load test Telepathy

Each client sends 100 bytes 14 per second and the server echoes back the same message.

for 1000 clients we have:

Test Computer: 2015 Macbook Pro with a 2,2 GHz Intel Core i7 processor.<br/>
Test Results:<br/>

| Clients | CPU Usage | Ram Usage | Bandwidth Client+Server  | Result |
| ------- | ----------| --------- | ------------------------ | ------ |
|   128   |        7% |     26 MB |         1-2 MB/s         | Passed |
|   500   |       28% |     51 MB |         3-4 MB/s         | Passed |
|  1000   |       42% |     75 MB |         3-5 MB/s         | Passed |

_Note: results will be significantly better on a really powerful server.

## Running the server ##

To run a server,  do:
```
cd bin/Debug
mono LoadTest.exe server 1337
```

## Running the clients ##

To run 1000 clients connected to localhost run:

```
cd bin/Debug
mono LoadTest.exe client 127.0.0.1 1337 1000
```

## Running both ##

If you want to run both the server and clients

```
cd bin/Debug
mono LoadTest.exe
```


## Troubleshooting ##

If you run this on a mac and you get the following error:
```
Unhandled Exception:
System.Net.Sockets.SocketException (0x80004005): Too many open files
```

You will need to raise the open file limit.  Issue the following command:

```
ulimit -n 2048
```
