import subprocess, sys, os, random
random.seed(1)
D=sys.argv[1]
def fi(path, data):
    subprocess.run(["git","init","-q","--initial-branch=main",path],check=True)
    subprocess.run(["git","fast-import","--quiet"],cwd=path,input=data,check=True)
    subprocess.run(["git","reset","-q","--hard"],cwd=path,check=True)
def b(s): return s if isinstance(s,bytes) else s.encode()
# (a) branchy graph
out=[]; N=50000; mark=0; marks=[]
def commit(ref,m,msg,frm=None,merge=None,t=1600000000):
    s=f"commit {ref}\nmark :{m}\ncommitter T <t@t> {t+m*60} +0000\ndata {len(msg)}\n{msg}\n"
    if frm: s+=f"from :{frm}\n"
    if merge: s+=f"merge :{merge}\n"
    c=f"x{m}"
    s+=f"M 100644 inline f{m%50}.txt\ndata {len(c)}\n{c}\n\n"
    return s
parts=[]
tips={"main":None}
branches=[]
for m in range(1,N+1):
    if m%50==0 and m>100:
        # branch off an older commit, then merge
        base=random.randint(max(1,m-400),m-2)
        parts.append(commit(f"refs/heads/side{m}",m,f"Side commit {m}",frm=base))
        if m%100==0:
            parts.append(commit("refs/heads/main",m+0 if False else N+m,f"Merge side {m}",frm=tips["main"],merge=m)) if False else None
        branches.append(m)
        marks.append(m)
        continue
    prev=tips["main"]
    parts.append(commit("refs/heads/main",m,f"Commit number {m} with a reasonably long subject line to measure",frm=prev))
    tips["main"]=m
# merges: add merge commits on main
mm=N
for i,sb in enumerate(branches):
    if i%2==0:
        mm+=1
        parts.append(commit("refs/heads/main",mm,f"Merge branch side{sb}",frm=tips["main"],merge=sb)); tips["main"]=mm
# tags
for i in range(0,N,60):
    k=i+1
    if k in branches: continue
    parts.append(f"reset refs/tags/v{i}\nfrom :{k}\n\n")
parts=[p for p in parts if p]
fi(D+"/graph",b("".join(parts)))
print("graph done", len(branches))
