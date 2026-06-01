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

gameobjects = {}
for fid, o in objs.items():
    if o['classid'] == '1':
        name_m = re.search(r'^\s+m_Name:\s*(.*)$', o['body'], re.M)
        name = name_m.group(1).strip() if name_m else '?'
        comps = re.findall(r'component:\s*\{fileID:\s*(\d+)\}', o['body'])
        gameobjects[fid] = {'name': name, 'components': comps}

# component owner reverse map and class info
def comp_class(cfid):
    o = objs.get(cfid)
    if not o: return None,None
    cls = o['classid']
    if cls == '114':
        guid_m = re.search(r'm_Script:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]+)', o['body'])
        return cls, guid_m.group(1) if guid_m else None
    return cls, None

# build path -> (go, components-by-class)
transforms = {}
for fid, o in objs.items():
    if o['classid'] in ('224','4'):
        body = o['body']
        go = re.search(r'm_GameObject:\s*\{fileID:\s*(\d+)\}', body).group(1)
        father = re.search(r'm_Father:\s*\{fileID:\s*(\d+)\}', body).group(1)
        children = []
        if 'm_Children:' in body:
            seg = body.split('m_Children:')[1].split('m_Father:')[0]
            children = re.findall(r'-\s*\{fileID:\s*(\d+)\}', seg)
        transforms[fid] = {'go':go,'parent':father,'children':children}

go_to_tr = {t['go']:k for k,t in transforms.items()}
roots = [t for t in transforms if transforms[t]['parent']=='0']

def find_path(p, parent_tr):
    parts = p.split('/')
    cur = parent_tr
    for part in parts:
        found = None
        for c in transforms[cur]['children']:
            cgo = transforms[c]['go']
            if gameobjects[cgo]['name'] == part:
                found = c; break
        if not found: return None
        cur = found
    return cur

# Use the root (PetTJUIForm) as base
root = roots[0]
print('ROOT:', root, gameobjects[transforms[root]['go']]['name'])

# Helper: list components on a path
def info(label, path):
    tr = find_path(path, root) if path else root
    if tr is None:
        print(f'{label}: NOT FOUND ({path})')
        return None
    go = transforms[tr]['go']
    comps = gameobjects[go]['components']
    print(f'{label}: path="{path}" go={go} tr={tr}')
    for c in comps:
        cls,guid = comp_class(c)
        suf = f' guid={guid}' if guid else ''
        print(f'    comp {c} cls={cls}{suf}')
    return tr

info('GoSelect', 'GoSelect')
info('GoSelect/Image', 'GoSelect/Image')
info('GoNoSelect', 'GoNoSelect')
info('GoNoSelect/Image (1)', 'GoNoSelect/Image (1)')
info('Content', 'PetTJ/Scroll View/Viewport/Content')
info('GoPet', 'PetTJ/Scroll View/Viewport/Content/GoPet')
info('GoPetccw', 'PetTJ/Scroll View/Viewport/Content/GoPetccw')
info('GoPetccw/ImgName/Text', 'PetTJ/Scroll View/Viewport/Content/GoPetccw/ImgName/Text (TMP)')
info('GoPetccw/Image', 'PetTJ/Scroll View/Viewport/Content/GoPetccw/Image')
info('GoPetDetailed', 'GoPetDetailed')
info('GoPetDetailed Output1', 'GoPetDetailed/PetDetailedBJ/PetDetailed/Output/Output1')
info('GoPetDetailed Output2', 'GoPetDetailed/PetDetailedBJ/PetDetailed/Output/Output2')
info('GoPetDetailed (1)', 'GoPetDetailed (1)')
info('GoPetDetailed (1)/PetDetailed/TxtName', 'GoPetDetailed (1)/PetDetailedBJ/PetDetailed/TxtName')
info('GoPetDetailed (1)/PetDetailed/TxtQuality', 'GoPetDetailed (1)/PetDetailedBJ/PetDetailed/TxtQuality')
info('GoPetDetailed (1)/PetDetailed/TxtProperty', 'GoPetDetailed (1)/PetDetailedBJ/PetDetailed/TxtProperty')
info('GoPetDetailed (1)/PetDetailed/TxtIntroduce', 'GoPetDetailed (1)/PetDetailedBJ/PetDetailed/TxtIntroduce')
info('GoPetDetailed (1)/PetDetailed/TxtOccurrenceConditions', 'GoPetDetailed (1)/PetDetailedBJ/PetDetailed/TxtOccurrenceConditions')
info('GoPetDetailed (1)/PetDetailed/Image', 'GoPetDetailed (1)/PetDetailedBJ/PetDetailed/Image')
