"""Configure only the already copied lab session: quiet virtual output, synthetic library."""
import os
from pathlib import Path
import xml.etree.ElementTree as ET

lab=Path(os.environ["LOCALAPPDATA"])/"Cratebase"/"traktor-lab"
root=lab/"user"/"Documents"/"Native Instruments"/"Traktor 4.5.1"
settings=root/"Traktor Settings.tsi"
tree=ET.parse(settings)
changes={"Audio.DeviceName.Win":"Voicemeeter Virtual ASIO","Audio.DriverName.Win":"ASIO","Status.Master Volume":"0","BackgroundThread.AutoAnalyze":"0","BackgroundThread.AutoAnalyzeOnLoad":"0","Browser.Dir.GeneratedStems":str(lab/'stems'),"Stems.DefaultLoad.Stems":"1"}
for entry in tree.iter("Entry"):
    if entry.get("Name") in changes:entry.set("Value",changes[entry.get("Name")])
tree.write(settings,encoding="utf-8",xml_declaration=True)
nml=ET.Element("NML",VERSION="19");ET.SubElement(nml,"HEAD",COMPANY="Native Instruments",PROGRAM="Traktor")
collection=ET.SubElement(nml,"COLLECTION",ENTRIES="2")
keys=[]
for name,title in (("Lab Original.wav","LAB Original Stereo"),("Lab Four Tones.stem.mp4","LAB Four Known Stems")):
    file=lab/"fixtures"/name;volume=file.drive;directory="/:"+"/:".join(file.parent.parts[1:])+"/:";key=volume+directory+name;keys.append(key)
    entry=ET.SubElement(collection,"ENTRY",TITLE=title,ARTIST="Cratebase Lab")
    ET.SubElement(entry,"LOCATION",DIR=directory,FILE=name,VOLUME=volume,VOLUMEID="")
    ET.SubElement(entry,"INFO",GENRE="Lab",PLAYTIME="90",PLAYTIME_FLOAT="90.000000",BITRATE="2116",RANKING="0")
    ET.SubElement(entry,"TEMPO",BPM="120.000000",BPM_QUALITY="100")
playlists=ET.SubElement(nml,"PLAYLISTS");node=ET.SubElement(playlists,"NODE",TYPE="FOLDER",NAME="$ROOT");children=ET.SubElement(node,"SUBNODES",COUNT="1");playlist_node=ET.SubElement(children,"NODE",TYPE="PLAYLIST",NAME="LAB Fixtures");playlist=ET.SubElement(playlist_node,"PLAYLIST",ENTRIES="2",TYPE="LIST",UUID="CEED0FABE00000000000000000000001")
for key in keys:ET.SubElement(ET.SubElement(playlist,"ENTRY"),"PRIMARYKEY",TYPE="TRACK",KEY=key)
ET.ElementTree(nml).write(root/"collection.nml",encoding="utf-8",xml_declaration=True)
print("Lab only: original+stem fixtures; master volume zero; virtual ASIO output selected.")
