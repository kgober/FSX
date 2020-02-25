# FSX
File System Exchange is a utility to access data stored in disk images, using an interactive command-line style interface.  The implementation language is C#, targeting the Microsoft .NET Framework 2.0.

Currently the following types of volumes are supported:
* Unix v5, v6, and v7
* Unix tar files
* RT-11
* Files-11 (a.k.a. ODS-1, the RSX-11 file system format)
* Commodore 1541/2040/4040/8050/8250

The following storage formats are supported:
* ImageDisk (.IMD) files
* TeleDisk (.TD0) files
* gzip (.gz) compressed files
* Compress (.Z) compressed files
* .D64/.D67/.D80/.D82 files
* raw block images

Support for additional volumes and file formats is planned, but in no particular order.  Feedback about areas to focus on next is welcomed.

To use FSX, simply run it from a console window such as the Windows Command Prompt.  At the FSX> prompt, any of the following commands are accepted:
* load|mount id pathname[ \<opts\>] - mount file 'pathname' as volume 'id:'
* save|write id pathname - export image of volume 'id:' to file 'pathname'
* unload|unmount|umount id - unmount volume 'id:'
* vols|volumes - show mounted volumes
* info [id:] - show volume information
* dirs - show current working directory for each mounted volume
* pwd - show current working directory on current volume
* id: - change current volume to 'id:'
* cd [id:]dir - change current directory
* dir|ls [id:]pattern - show directory
* dumpdir [id:]pattern - show raw directory data
* type|cat [id:]file - show file as text
* zcat [id:]file - show compressed file as text
* dump|od [id:]file - show file as a hex dump
* save|write [id:]file pathname - export image of file 'file' to file 'pathname'
* out pathname - redirect output to 'pathname' (omit pathname to reset)
* verb|verbose n - set verbosity level (default 0)
* deb|debug n - set debug level (default 0)
* source|. pathname - read commands from 'pathname'
* help - show this text
* exit|quit - exit program

load/mount options:
* \<skip=num\> - skip first 'num' bytes of 'pathname'
* \<pad=num\> - pad end of 'pathname' with 'num' zero bytes
* \<type=name\> - mount as a file system of type 'name'
