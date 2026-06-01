# -*- coding: utf-8 -*-
import re, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

path = r'Assets/_Game/Resources/Prefabs/UI/Pet/PetTJUIForm.prefab'
text = open(path, encoding='utf-8').read()

docs = re.split(r'^--- !u!(\d+) &(\d+)', text, flags=re.M)
objs = {}
i = 1
while i < len(docs):
    objs[docs[i+1]] = {'classid': docs[i], 'body': docs[i+2]}
    i += 3

# Inspect a few specific Images for Sprite reference
def show(fid, label):
    o = objs.get(fid)
    if not o:
        print('NOT FOUND', fid); return
    print(f'--- {label} fileID={fid} cls={o["classid"]}')
    for line in o['body'].split('\n')[:60]:
        if any(k in line for k in ['m_Sprite','m_Color','m_Type','m_Material','m_Image','m_RaycastTarget']):
            print('   ', line.strip())

# Output1/Output2 image MB
show('5947147496952807283', 'GoPetDetailed/Output/Output1 Image MB')
show('7341709037859868632', 'GoPetDetailed/Output/Output2 Image MB')
# GoPetDetailed (1)/Image — user said this displays the produce icon
show('7315282447393648578', 'GoPetDetailed (1)/PetDetailed/Image MB')
# GoPetDetailed root Button: clicking the panel closes it (existing)
show('5547409321206609335', 'GoPetDetailed Button MB')
show('7166659091576481027', 'GoPetDetailed (1) Button MB')

# Find ScrollRect fileID
for fid,o in objs.items():
    if o['classid']=='114' and '1aa08ab6' in o['body']:
        print('ScrollRect MB fileID =', fid)
