import math, sys, random

rows = [l.strip().split(',') for l in open('train.csv') if l.strip()]
X = [[float(v) for v in r[:12]] for r in rows]
y = [int(r[12]) for r in rows]
sc = [int(r[13]) for r in rows]
names = ['day-no-terrain','day-light-terrain','day-heavy-terrain',
         'overcast-no-terrain','overcast-light-terrain','dusk-no-terrain',
         'dusk-light-terrain','dusk-heavy-terrain','night-heavy-terrain',
         'lava-blue-feather','NEG-day','NEG-dusk','NEG-dusk-heavy','NEG-night']

def sig(z): return 1/(1+math.exp(-max(-30,min(30,z))))

def train(tr, d, reg=0.001, epochs=6000, lr=0.5):
    n=len(tr)
    mu=[sum(X[i][j] for i in tr)/n for j in range(d)]
    sd=[]
    for j in range(d):
        v=sum((X[i][j]-mu[j])**2 for i in tr)/n
        sd.append(math.sqrt(v) if v>1e-12 else 1.0)
    Xs={i:[(X[i][j]-mu[j])/sd[j] for j in range(d)] for i in tr}
    w=[0.0]*d; b=0.0
    for ep in range(epochs):
        gw=[0.0]*d; gb=0.0
        for i in tr:
            z=b+sum(w[j]*Xs[i][j] for j in range(d))
            e=sig(z)-y[i]
            for j in range(d): gw[j]+=e*Xs[i][j]
            gb+=e
        for j in range(d): w[j]-=lr*(gw[j]/len(tr)+reg*w[j])
        b-=lr*(gb/len(tr))
    return w,b,mu,sd

def score(w,b,mu,sd,i):
    z=b+sum(w[j]*(X[i][j]-mu[j])/sd[j] for j in range(len(w)))
    return sig(z)

def evaluate(w,b,mu,sd,idx,thr=0.5):
    tp=fp=fn=tn=0
    for i in idx:
        p = 1 if score(w,b,mu,sd,i) > thr else 0
        if p==1 and y[i]==1: tp+=1
        elif p==1 and y[i]==0: fp+=1
        elif p==0 and y[i]==1: fn+=1
        else: tn+=1
    prec=tp/(tp+fp) if tp+fp else 0.0
    rec=tp/(tp+fn) if tp+fn else 0.0
    f1=2*prec*rec/(prec+rec) if prec+rec else 0.0
    return tp,fp,fn,tn,prec,rec,f1

d=12
# Leave-one-scene-out cross validation: the honest estimate of generalisation
print("=== Leave-one-scene-out CV (train on 9 scenes, test on the held-out one) ===")
print(f"{'held-out scene':<26}{'prec':>8}{'recall':>9}{'F1':>8}   (pos in scene)")
f1s=[]
for hold in range(len(names)):
    tr=[i for i in range(len(y)) if sc[i]!=hold]
    te=[i for i in range(len(y)) if sc[i]==hold]
    if not te or sum(y[i] for i in te)==0: 
        print(f"{names[hold]:<26}{'--':>8}{'--':>9}{'--':>8}   {sum(y[i] for i in te)}")
        continue
    w,b,mu,sd = train(tr,d)
    tp,fp,fn,tn,prec,rec,f1 = evaluate(w,b,mu,sd,te)
    f1s.append(f1)
    print(f"{names[hold]:<26}{prec:8.3f}{rec:9.3f}{f1:8.3f}   {sum(y[i] for i in te)}")
print(f"\nmean F1 over held-out scenes: {sum(f1s)/len(f1s):.3f}")

# Final model on ALL data (what we would ship)
print("\n=== Final model trained on all scenes ===")
allidx=list(range(len(y)))
w,b,mu,sd = train(allidx,d)
tp,fp,fn,tn,prec,rec,f1 = evaluate(w,b,mu,sd,allidx)
print(f"in-sample: prec={prec:.3f} rec={rec:.3f} F1={f1:.3f}")

with open('weights.txt','w') as f:
    f.write(','.join(f'{v:.8f}' for v in w+[b]))
with open('scaler.txt','w') as f:
    f.write(','.join(f'{v:.8f}' for v in mu)+'|'+','.join(f'{v:.8f}' for v in sd))
print("\nweights written (", len(w), "coeffs + bias )")
