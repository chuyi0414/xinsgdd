# -*- coding: utf-8 -*-
import re, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

path = r'Assets/_Game/Resources/Prefabs/UI/Pet/PetTJUIForm.prefab'
text = open(path, encoding='utf-8').read()

# Split into documents
docs = re.split(r'^--- !u!(\d+) &(\d+)', text, flags=re.M)
# docs[0] is header; then groups of (classid, fileid, body)
objs = {}  # fileid -> dict
order = []
i = 1
while i < len(docs):
    classid = docs[i]
    fileid = docs[i+1]
    body = docs[i+2]
    objs[fileid] = {'classid': classid, 'body': body}
    order.append(fileid)
    i += 3

gameobjects = {}   # fileid -> {name, components:[], transform:fileid}
transforms = {}    # transform fileid -> {go, parent, children:[]}
comp_owner = {}    # component fileid -> go fileid

for fid, o in objs.items():
    body = o['body']
    cls = o['classid']
    if cls == '1':  # GameObject
        name_m = re.search(r'^\s+m_Name:\s*(.*)$', body, re.M)
        name = name_m.group(1).strip() if name_m else '?'
        comps = re.findall(r'component:\s*\{fileID:\s*(\d+)\}', body)
        gameobjects[fid] = {'name': name, 'components': comps}
    
for fid, o in objs.items():
    body = o['body']
    cls = o['classid']
    if cls in ('224', '4'):  # RectTransform or Transform
        go_m = re.search(r'm_GameObject:\s*\{fileID:\s*(\d+)\}', body)
        father_m = re.search(r'm_Father:\s*\{fileID:\s*(\d+)\}', body)
        children = re.findall(r'-\s*\{fileID:\s*(\d+)\}', body.split('m_Children:')[1].split('m_Father:')[0]) if 'm_Children:' in body else []
        transforms[fid] = {
            'go': go_m.group(1) if go_m else None,
            'parent': father_m.group(1) if father_m else '0',
            'children': children,
            'classid': cls
        }

# Map go -> transform
go_to_tr = {}
for tfid, t in transforms.items():
    if t['go']:
        go_to_tr[t['go']] = tfid

# component class for each component fileid
def comp_label(cfid):
    o = objs.get(cfid)
    if not o:
        return f'?{cfid}'
    cls = o['classid']
    body = o['body']
    if cls == '114':  # MonoBehaviour
        guid_m = re.search(r'm_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]+)', body)
        guid = guid_m.group(1) if guid_m else '?'
        return f'MB({guid[:8]})'
    names = {'222':'CanvasRenderer','114':'MB','223':'Canvas','225':'CanvasGroup',
             '20':'Camera','1':'GO'}
    return {'222':'CanvasRenderer','223':'Canvas','225':'CanvasGroup'}.get(cls, f'cls{cls}')

# Find roots
all_tr = set(transforms.keys())
child_set = set()
for t in transforms.values():
    for c in t['children']:
        child_set.add(c)
roots = [t for t in all_tr if transforms[t]['parent'] == '0']

def walk(tfid, depth):
    t = transforms[tfid]
    go = t['go']
    name = gameobjects.get(go, {}).get('name', '?') if go else '?'
    comps = gameobjects.get(go, {}).get('components', []) if go else []
    labels = []
    for c in comps:
        l = comp_label(c)
        if l and l not in ('CanvasRenderer',):
            labels.append(l)
    print('  '*depth + f'- {name}  [go={go} tr={tfid}] {labels}')
    for c in t['children']:
        if c in transforms:
            walk(c, depth+1)

for r in roots:
    walk(r, 0)
