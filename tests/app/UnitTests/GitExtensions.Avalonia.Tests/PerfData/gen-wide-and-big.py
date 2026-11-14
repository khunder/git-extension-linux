import subprocess, sys, os, random
D=sys.argv[1]
def init(p):
    subprocess.run(["git","init","-q","--initial-branch=main",p],check=True)
    subprocess.run(["git","config","user.name","T"],cwd=p); subprocess.run(["git","config","user.email","t@t"],cwd=p)
# (b)
p=D+"/wide"; init(p)
for i in range(20000):
    d=f"{p}/d{i//200}"; os.makedirs(d,exist_ok=True)
    open(f"{d}/file{i}.txt","w").write(f"content {i}\n"*3)
subprocess.run(["git","add","-A"],cwd=p,check=True); subprocess.run(["git","commit","-qm","wide"],cwd=p,check=True)
for i in range(0,20000,20):  # 1000 modified
    open(f"{p}/d{i//200}/file{i}.txt","a").write("mod\n")
for i in range(1000):
    open(f"{p}/new{i}.txt","w").write("new\n")
# (c)
p=D+"/big"; init(p)
lines=[f"line {i} "+"x"*18+"\n" for i in range(200000)]
open(p+"/big.txt","w").write("".join(lines))
open(p+"/big.bin","wb").write(os.urandom(30*1024*1024))
open(p+"/small.txt","w").write("".join(f"s{i}\n" for i in range(10000)))
open(p+"/longline.txt","w").write("short\n")
subprocess.run(["git","add","-A"],cwd=p,check=True); subprocess.run(["git","commit","-qm","big"],cwd=p,check=True)
open(p+"/big.txt","w").write("".join(lines[:50000]+["CHANGED %d\n"%i for i in range(50000)]+lines[100000:]))
open(p+"/small.txt","w").write("".join(f"t{i}\n" for i in range(10000)))   # 10000-line diff (10k -, 10k +)
open(p+"/big.bin","ab").write(os.urandom(1000))
open(p+"/longline.txt","w").write("A"*(3*1024*1024)+"\nshort\n")
open(p+"/nul.txt","wb").write(b"abc\0def\nline2\xff\xfe\n")
