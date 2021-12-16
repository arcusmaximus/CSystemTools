# CSystemTools
Script and image unpacking and repacking tool for the Cyberworks "C,system" visual novel engine.

Note: the tool has had only limited testing and doesn't work for all games.

## Overview

Games based on this engine can have the following files:

* Arc00.dat: general configuration (e.g. window title, font size and so on)
* Arc01.dat: scenario file table
* Arc04.dat: scenario file content
* Arc02.dat: image file table
* Arc05.dat, Arc05a.dat, Arc05b.dat: image file content
* Arc03.dat: audio file table
* Arc06.dat: audio file content
* Arc07.dat: unknown table
* Arc08.dat: unknown content
* Arc09.dat: unknown table

.csa scenario files extracted from Arc01.dat/Arc04.dat can be translated using [VNTranslationTools](https://github.com/arcusmaximus/VNTranslationTools).

Images will be automatically converted to and from PNG.

Audio files are currently not supported (the obfuscated OGGs will be extracted as-is).

## Command line

```
CSystemArc unpack index.dat content1.dat content2.dat ... folder
```
Extract the specified archives to a folder. Example: `unpack Arc01.dat Arc04.dat scenarios`

```
CSystemArc pack folder index.dat content1.dat content2.dat ...
```
Pack a folder into one or more archive files. The archive files will be completely overwritten (files that are in the original archives but not in the folder will be lost). Example: `pack scenarios Arc01.dat Arc04.dat`

```
CSystemArc readconfig Arc00.dat config.xml
```
Convert the binary configuration file to a more or less human-readable (and editable) XML file.
Some known settings:
* AT/AL/AB/AR: message window top/left/bottom/right
* M: maximum number of lines
* F: font size
* X: character spacing
* Y: line spacing

```
CSystemArc writeconfig config.xml Arc00.dat
```
Convert the above XML file back into a binary file that works with the game.
