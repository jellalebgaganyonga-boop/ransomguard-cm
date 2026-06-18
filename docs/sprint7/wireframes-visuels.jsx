import { useState } from "react";
import { ShieldAlert, LayoutDashboard, Monitor, Users, ScrollText, Settings, Bell, Globe, LogOut, User, ChevronDown, AlertCircle, AlertTriangle, Info, Eye, EyeOff, Search, ChevronLeft, Shield, ShieldOff, Check, FileText, Clock, Copy, ArrowUp, ArrowDown, X, Wifi, WifiOff, Download, ExternalLink, Activity } from "lucide-react";

/*
 ╔══════════════════════════════════════════════════════════════════╗
 ║  RANSOMGUARD-CM — MAQUETTES VISUELLES COMPLÈTES v2              ║
 ║                                                                  ║
 ║  Organisation: PAR RÔLE → PAR ÉCRAN → PAR ÉTAT                 ║
 ║                                                                  ║
 ║  Source: PRD v2 (22 User Stories, 6 EPICs)                      ║
 ║          + Definition Phase Day 4 (IA, User Flows, Task Flows)  ║
 ║          + Design Phase Day 6-10 (Tokens, Wireframes, Hi-Fi)    ║
 ║          + Discovery Phase Day 1-3 (Personas, JTBD, OST)        ║
 ║                                                                  ║
 ║  INCOHÉRENCE IDENTIFIÉE:                                        ║
 ║  US4.1 AC4.1.4 dit analyst peut [Send command]                  ║
 ║  US4.3 AC4.3.2 dit analyst NE PEUT PAS isoler (403)            ║
 ║  Flow F6 dit analyst + admin peuvent isoler                     ║
 ║  → DÉCISION TECH LEAD: PRD v2 AC4.3.2 fait autorité.           ║
 ║    Analyst NE PEUT PAS isoler. Admin uniquement.                ║
 ╚══════════════════════════════════════════════════════════════════╝
*/

const T = {
  blue40:"#3A66A6",blue30:"#284878",blue50:"#4D80C8",blue95:"#ECF2FB",
  gray10:"#0F1115",gray20:"#1A1D24",gray40:"#3D424E",gray50:"#5B6170",gray60:"#7E8493",gray80:"#CACDD4",gray90:"#E4E6EA",gray95:"#F1F2F5",gray99:"#FAFBFC",
  red40:"#B12626",red50:"#D63838",red95:"#FDECEC",
  orange40:"#B26819",orange50:"#D9842B",orange95:"#FDF4E7",
  yellow20:"#4F3D0E",yellow50:"#C9A035",yellow95:"#FCF5E8",
  green40:"#2A9051",green50:"#3AAF65",green95:"#E8F6EE",
  white:"#FFFFFF",
};

// ── Shared Primitives ──
const Badge=({children,bg,color,border})=><span style={{display:"inline-flex",alignItems:"center",gap:3,padding:"2px 8px",borderRadius:4,fontSize:11,fontWeight:600,letterSpacing:"0.04em",textTransform:"uppercase",background:bg||T.gray95,color:color||T.gray40,border:border?`1px solid ${border}`:"1px solid transparent"}}>{children}</span>;
const Score=({n})=>{const bg=n>85?T.red50:n>=15?T.orange50:T.gray60;return<span style={{display:"inline-flex",minWidth:36,justifyContent:"center",padding:"2px 8px",borderRadius:6,fontSize:12,fontWeight:700,color:T.white,background:bg}}>{n}</span>};
const SevBadge=({l})=>{const c={critical:{bg:T.red95,c:T.red50,b:T.red40,i:AlertCircle,t:"CRITIQUE"},high:{bg:T.orange95,c:T.orange50,b:T.orange40,i:AlertTriangle,t:"HAUT"},medium:{bg:T.yellow95,c:T.yellow20,b:T.yellow50,i:AlertTriangle,t:"MOYEN"},low:{bg:T.blue95,c:T.blue40,b:T.blue30,i:Info,t:"FAIBLE"},info:{bg:T.gray95,c:T.gray50,b:T.gray60,i:Info,t:"INFO"}}[l];const I=c.i;return<Badge bg={c.bg} color={c.c} border={c.b}><I size={11}/>{c.t}</Badge>};
const StatBadge=({s})=>{const c={new:{bg:T.red95,c:T.red50,t:"Nouveau"},acknowledged:{bg:T.blue95,c:T.blue40,t:"En cours"},closed:{bg:T.gray95,c:T.gray60,t:"Fermé"},isolated:{bg:T.red95,c:T.red40,t:"Isolé"},isolation_pending:{bg:T.orange95,c:T.orange50,t:"Isolation en cours"}}[s]||{bg:T.gray95,c:T.gray60,t:s};return<Badge bg={c.bg} color={c.c}>{c.t}</Badge>};
const Btn=({children,v="primary",disabled,full,sz="md",onClick})=>{const s={primary:{bg:T.blue40,c:T.white,bd:"none"},secondary:{bg:"transparent",c:T.gray10,bd:`1px solid ${T.gray80}`},danger:{bg:T.red50,c:T.white,bd:"none"},ghost:{bg:"transparent",c:T.gray40,bd:"none"}}[v];const p=sz==="sm"?"5px 10px":sz==="lg"?"12px 24px":"8px 16px";return<button onClick={onClick} disabled={disabled} style={{display:"inline-flex",alignItems:"center",gap:6,padding:p,borderRadius:6,fontSize:sz==="sm"?12:14,fontWeight:500,cursor:disabled?"not-allowed":"pointer",opacity:disabled?.5:1,background:s.bg,color:s.c,border:s.bd,width:full?"100%":"auto",justifyContent:full?"center":"flex-start"}}>{children}</button>};
const Card=({children,style={}})=><div style={{background:T.white,border:`1px solid ${T.gray90}`,borderRadius:8,padding:20,boxShadow:"0 1px 2px rgba(15,17,21,.05)",...style}}>{children}</div>;
const KPI=({label,value,sub})=><Card><p style={{fontSize:11,fontWeight:600,textTransform:"uppercase",letterSpacing:".04em",color:T.gray40,margin:0}}>{label}</p><p style={{fontSize:28,fontWeight:700,color:T.gray10,margin:"6px 0 0",fontVariantNumeric:"tabular-nums"}}>{value}</p>{sub&&<p style={{fontSize:12,color:T.gray60,margin:"4px 0 0"}}>{sub}</p>}</Card>;
const Dot=({c})=><span style={{width:8,height:8,borderRadius:4,background:c,display:"inline-block"}}/>;
const Inp=({label,placeholder,type="text"})=>{const[show,setShow]=useState(false);return<div style={{display:"flex",flexDirection:"column",gap:4}}>{label&&<label style={{fontSize:13,fontWeight:500,color:T.gray40}}>{label}</label>}<div style={{position:"relative"}}><input type={type==="password"&&show?"text":type} placeholder={placeholder} style={{width:"100%",padding:"8px 12px",paddingRight:type==="password"?36:12,borderRadius:6,border:`1px solid ${T.gray80}`,fontSize:14,color:T.gray10,outline:"none",boxSizing:"border-box"}}/>{type==="password"&&<button onClick={()=>setShow(!show)} style={{position:"absolute",right:8,top:8,background:"none",border:"none",cursor:"pointer",color:T.gray60}}>{show?<EyeOff size={16}/>:<Eye size={16}/>}</button>}</div></div>};

// ── Alert Data ──
const ALERTS=[
  {id:"a1f3",score:92,sev:"critical",host:"PEDIATRIE-02",module:"SENTINEL",age:"14 min",summary:"Chiffrement détecté · 12 fichiers · powershell.exe"},
  {id:"b2c5",score:74,sev:"high",host:"CARDIO-05",module:"USB GUARD",age:"47 min",summary:"Périphérique USB non autorisé · VID:1234"},
  {id:"c3d7",score:38,sev:"medium",host:"ADMIN-12",module:"ENTROPY",age:"2 h",summary:"Activité fichier inhabituelle · 23 fichiers"},
  {id:"d4e9",score:22,sev:"low",host:"IMAGERIE-03",module:"GENEALOGY",age:"5 h",summary:"Processus parent inhabituel"},
];
const AGENTS=[
  {host:"PEDIATRIE-02",os:"Win 10",ver:"v0.7.0",hb:"12 s",st:"online"},
  {host:"CARDIO-05",os:"Win 10",ver:"v0.7.0",hb:"18 s",st:"online"},
  {host:"ADMIN-12",os:"Win 11",ver:"v0.7.0",hb:"23 s",st:"online"},
  {host:"IMAGERIE-03",os:"Win 7",ver:"v0.6.5",hb:"1 min",st:"online"},
  {host:"LABO-08",os:"Win 10",ver:"v0.7.0",hb:"45 min",st:"stale"},
  {host:"PHARMA-04",os:"Win 10",ver:"v0.6.5",hb:"2 h",st:"offline"},
];

// ── Layout Shell ──
function Shell({role,nav,notif=0,children}){
  const items=[
    {k:"dashboard",i:LayoutDashboard,l:"Tableau de bord",r:["admin","analyst","auditor"]},
    {k:"alerts",i:ShieldAlert,l:"Alertes",r:["admin","analyst","auditor"],badge:3},
    {k:"agents",i:Monitor,l:"Agents",r:["admin","analyst","auditor"]},
    {k:"users",i:Users,l:"Utilisateurs",r:["admin"]},
    {k:"audit",i:ScrollText,l:"Journal d'audit",r:["admin","auditor"]},
    {k:"settings",i:Settings,l:"Paramètres",r:["admin"]},
  ].filter(x=>x.r.includes(role));
  const nm={admin:"Dr. Amani Nkomo",analyst:"Marc Tchoumi",auditor:"Sr. Jeanne Bilo'o"};
  const ini={admin:"AN",analyst:"MT",auditor:"JB"};
  return<div style={{display:"flex",flexDirection:"column",height:"100%",background:T.gray99,fontFamily:"system-ui,-apple-system,sans-serif"}}>
    <header style={{display:"flex",alignItems:"center",justifyContent:"space-between",height:56,padding:"0 20px",background:T.white,borderBottom:`1px solid ${T.gray90}`,boxShadow:"0 1px 2px rgba(15,17,21,.04)",flexShrink:0}}>
      <div style={{display:"flex",alignItems:"center",gap:10}}><span style={{fontSize:16,fontWeight:600,color:T.gray10}}>🛡 RansomGuard-CM</span><span style={{padding:"3px 10px",borderRadius:99,background:T.blue95,fontSize:11,fontWeight:500,color:T.blue40}}>Hôp. Rég. Yaoundé</span></div>
      <div style={{display:"flex",alignItems:"center",gap:10}}>
        <div style={{position:"relative"}}><Bell size={18} color={T.gray40}/>{notif>0&&<span style={{position:"absolute",top:-5,right:-5,background:T.red50,color:T.white,fontSize:9,fontWeight:700,borderRadius:9,padding:"1px 4px"}}>{notif}</span>}</div>
        <span style={{fontSize:12,color:T.gray40,display:"flex",alignItems:"center",gap:3}}><Globe size={14}/>FR</span>
        <div style={{display:"flex",alignItems:"center",gap:6}}>
          <div style={{width:28,height:28,borderRadius:14,background:T.blue40,display:"flex",alignItems:"center",justifyContent:"center",fontSize:10,fontWeight:700,color:T.white}}>{ini[role]}</div>
          <span style={{fontSize:12,fontWeight:500,color:T.gray10}}>{nm[role]}</span>
        </div>
      </div>
    </header>
    <div style={{display:"flex",flex:1,overflow:"hidden"}}>
      <nav style={{width:220,background:T.gray99,borderRight:`1px solid ${T.gray90}`,padding:"12px 6px",flexShrink:0,overflow:"auto"}}>
        {items.map(x=>{const I=x.i;const a=nav===x.k;return<div key={x.k} style={{display:"flex",alignItems:"center",gap:10,padding:"7px 14px",borderRadius:6,marginBottom:1,borderLeft:a?`3px solid ${T.blue40}`:"3px solid transparent",background:a?T.blue95:"transparent",color:a?T.blue40:T.gray40,fontWeight:a?500:400,fontSize:13,cursor:"pointer"}}><I size={18}/><span style={{flex:1}}>{x.l}</span>{x.badge>0&&<span style={{background:T.red50,color:T.white,fontSize:10,fontWeight:700,borderRadius:8,padding:"1px 5px"}}>{x.badge}</span>}</div>})}
      </nav>
      <main style={{flex:1,overflow:"auto",padding:20}}><div style={{maxWidth:1200,margin:"0 auto"}}>{children}</div></main>
    </div>
    {/* Footer per IA §Global Footer */}
    <footer style={{height:32,display:"flex",alignItems:"center",justifyContent:"space-between",padding:"0 20px",borderTop:`1px solid ${T.gray90}`,background:T.white,fontSize:11,color:T.gray60,flexShrink:0}}>
      <span>v0.9.0</span>
      <span style={{display:"flex",alignItems:"center",gap:4}}><Dot c={T.green50}/>En ligne</span>
      <span style={{color:T.blue40,cursor:"pointer"}}>Documentation</span>
    </footer>
  </div>
}

// ═══════════════════════════════════════════════════
// RÔLE 1: TENANT_ADMIN (Dr. Amani Nkomo)
// IT: 7/10, Cybersec: 4/10, Fréquence: 2-4x/sem
// Quote: "Donnez-moi ce que je dois savoir, pas tout"
// ═══════════════════════════════════════════════════

function Admin_Dashboard_Normal(){return<Shell role="admin" nav="dashboard" notif={2}>
  <h1 style={{fontSize:22,fontWeight:600,color:T.gray10,margin:0}}>Tableau de bord exécutif</h1>
  <p style={{fontSize:13,color:T.gray40,margin:"4px 0 20px"}}>Vue d'ensemble de la sécurité de l'hôpital</p>
  {/* AC2.2.1: Status card — critical > 0 → red */}
  <Card style={{background:T.red95,borderLeft:`4px solid ${T.red50}`,marginBottom:20}}>
    <div style={{display:"flex",alignItems:"center",gap:10}}><AlertCircle size={22} color={T.red50}/><div><p style={{fontSize:15,fontWeight:600,color:T.red50,margin:0}}>3 INCIDENTS CRITIQUES REQUIÈRENT VOTRE ATTENTION</p><p style={{fontSize:12,color:T.gray40,margin:"3px 0 0"}}>Le plus récent : il y a 14 min sur PEDIATRIE-02</p></div></div>
    <Btn v="ghost" sz="sm">Voir les détails →</Btn>
  </Card>
  {/* AC2.2.2: Four KPI cards */}
  <div style={{display:"grid",gridTemplateColumns:"repeat(4,1fr)",gap:12,marginBottom:20}}>
    <KPI label="Postes protégés" value="47/47" sub="100% — Tous actifs"/>
    <KPI label="Alertes 24h" value="12" sub="⬆ +3 vs hier"/>
    <KPI label="Jours sans incident critique" value="0" sub="Reset aujourd'hui"/>
    <KPI label="Conformité ANTIC" value="98%" sub="✅ Conforme"/>
  </div>
  {/* AC2.2.3: Recent alerts widget — 5 most recent */}
  <Card style={{marginBottom:20}}>
    <div style={{display:"flex",justifyContent:"space-between",marginBottom:12}}><h3 style={{fontSize:14,fontWeight:600,color:T.gray10,margin:0}}>ALERTES RÉCENTES</h3><span style={{fontSize:12,color:T.blue40,cursor:"pointer"}}>Voir tout →</span></div>
    {ALERTS.map((a,i)=><div key={a.id} style={{display:"flex",alignItems:"center",gap:12,padding:"10px 0",borderTop:i?`1px solid ${T.gray90}`:"none"}}>
      <Score n={a.score}/><SevBadge l={a.sev}/><div style={{flex:1}}><span style={{fontFamily:"monospace",fontSize:12,fontWeight:600,color:T.gray10}}>{a.host}</span><span style={{fontSize:11,color:T.gray60,marginLeft:6}}>{a.module}</span><p style={{fontSize:12,color:T.gray40,margin:"2px 0 0"}}>{a.summary}</p></div><span style={{fontSize:11,color:T.gray60}}>{a.age}</span>
    </div>)}
  </Card>
  {/* AC2.2.4: Quick actions — admin-specific */}
  <div style={{display:"grid",gridTemplateColumns:"1fr 1fr",gap:12}}>
    <Card><h3 style={{fontSize:13,fontWeight:600,color:T.gray10,margin:"0 0 12px"}}>Actions rapides</h3><div style={{display:"flex",flexDirection:"column",gap:6}}><Btn v="secondary" full><Users size={14}/>Gérer utilisateurs</Btn><Btn v="secondary" full><ScrollText size={14}/>Consulter journal</Btn><Btn v="secondary" full disabled><FileText size={14}/>Rapport (Sprint 8)</Btn></div></Card>
    <Card><h3 style={{fontSize:13,fontWeight:600,color:T.gray10,margin:"0 0 12px"}}>Conformité</h3><p style={{fontSize:13,color:T.gray10,margin:0}}>Loi 2024/017: <span style={{color:T.green50,fontWeight:600}}>✅ Conforme</span></p><p style={{fontSize:12,color:T.gray40,margin:"6px 0"}}>Prochain audit: 15 sept 2026</p><Btn v="secondary" full disabled>Télécharger rapport (Sprint 8)</Btn></Card>
  </div>
</Shell>}

function Admin_Dashboard_AllClear(){return<Shell role="admin" nav="dashboard" notif={0}>
  <h1 style={{fontSize:22,fontWeight:600,color:T.gray10,margin:0}}>Tableau de bord exécutif</h1>
  <p style={{fontSize:13,color:T.gray40,margin:"4px 0 20px"}}>Vue d'ensemble de la sécurité de l'hôpital</p>
  {/* AC2.2.1: critical_24h == 0 → green */}
  <Card style={{background:T.green95,borderLeft:`4px solid ${T.green50}`,marginBottom:20}}>
    <div style={{display:"flex",alignItems:"center",gap:10}}><Shield size={22} color={T.green50}/><p style={{fontSize:15,fontWeight:600,color:T.green40,margin:0}}>TOUS LES SYSTÈMES NORMAUX</p></div>
    <p style={{fontSize:12,color:T.gray40,margin:"4px 0 0"}}>Aucun incident critique dans les dernières 24h</p>
  </Card>
  <div style={{display:"grid",gridTemplateColumns:"repeat(4,1fr)",gap:12}}>
    <KPI label="Postes protégés" value="47/47" sub="Tous actifs"/>
    <KPI label="Alertes 24h" value="2" sub="⬇ -5 vs hier"/>
    <KPI label="Jours sans incident critique" value="14" sub="Meilleur: 28j"/>
    <KPI label="Conformité ANTIC" value="100%" sub="✅ Conforme"/>
  </div>
</Shell>}

// Admin: Alert Detail — status=new (AC3.2.5: admin + new → [Acknowledge])
// Admin CAN isolate per AC4.3.1
function Admin_AlertDetail_New(){return<Shell role="admin" nav="alerts" notif={2}>
  <div style={{display:"flex",alignItems:"center",gap:6,marginBottom:16,color:T.blue40,fontSize:13,cursor:"pointer"}}><ChevronLeft size={14}/>Retour aux alertes</div>
  <div style={{fontSize:12,color:T.gray60,fontFamily:"monospace",marginBottom:6}}>Alerte #a1f3b8e2-... <Copy size={12} style={{cursor:"pointer"}}/></div>
  <div style={{display:"flex",alignItems:"center",gap:10,marginBottom:16,flexWrap:"wrap"}}><Score n={92}/><SevBadge l="critical"/><StatBadge s="new"/><span style={{fontSize:12,color:T.gray60,marginLeft:"auto"}}>Détecté il y a 14 min</span></div>
  <div style={{display:"grid",gridTemplateColumns:"1fr 1fr",gap:12,marginBottom:16}}>
    <div><span style={{fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>AGENT</span><p style={{fontSize:13,color:T.gray10,margin:"3px 0",fontFamily:"monospace"}}>PEDIATRIE-02 <span style={{fontFamily:"system-ui",color:T.gray40}}>· Pédiatrie</span></p></div>
    <div><span style={{fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>MODULE</span><p style={{fontSize:13,color:T.gray10,margin:"3px 0",fontWeight:600}}>SENTINEL</p></div>
  </div>
  {/* AC3.2.5: admin + new → Acknowledge. AC4.3.1: admin → Isolate visible */}
  <Card style={{background:T.orange95,borderLeft:`4px solid ${T.orange50}`,marginBottom:20}}>
    <div style={{display:"flex",gap:8,marginBottom:10}}><Btn v="primary">Prendre en charge</Btn><Btn v="secondary">Marquer faux positif</Btn></div>
    <div style={{display:"flex",alignItems:"center",gap:6}}><AlertTriangle size={16} color={T.orange50}/><span style={{fontSize:13}}>Action immédiate suggérée :</span></div>
    <div style={{marginTop:8}}><Btn v="danger"><ShieldOff size={14}/>Isoler PEDIATRIE-02 du réseau</Btn></div>
    <p style={{fontSize:10,color:T.gray60,margin:"6px 0 0",fontStyle:"italic"}}>⚡ Bouton Isoler visible: rôle = tenant_admin (AC4.3.1)</p>
  </Card>
  {/* Tabs */}
  <div style={{display:"flex",borderBottom:`1px solid ${T.gray90}`,marginBottom:16}}>{["Résumé","Chronologie","Artefacts (12)","Actions"].map((t,i)=><span key={t} style={{padding:"8px 14px",fontSize:13,fontWeight:i===0?500:400,color:i===0?T.blue40:T.gray40,borderBottom:i===0?`2px solid ${T.blue40}`:"none",cursor:"pointer"}}>{t}</span>)}</div>
  {/* Inverted pyramid — SOL A2 */}
  <Card>
    <h3 style={{fontSize:14,fontWeight:600,color:T.gray10,margin:"0 0 10px",textTransform:"uppercase"}}>QUE S'EST-IL PASSÉ ?</h3>
    <p style={{fontSize:14,color:T.gray10,lineHeight:1.7,margin:"0 0 12px",maxWidth:680}}>Le module SENTINEL a détecté qu'un fichier canary a été modifié par <code style={{background:T.gray95,padding:"1px 4px",borderRadius:3,fontSize:12}}>powershell.exe</code> (PID 4892). Entropie: <strong>7.92</strong> sur 12 fichiers en 4 secondes.</p>
    <div style={{display:"flex",alignItems:"center",gap:10,marginBottom:16}}><span style={{fontSize:12,color:T.gray40}}>Confiance: 94%</span><div style={{width:160,height:6,background:T.gray90,borderRadius:3}}><div style={{width:"94%",height:"100%",background:T.green50,borderRadius:3}}/></div></div>
    <div style={{borderTop:`1px solid ${T.gray90}`,paddingTop:16,marginBottom:16}}><h3 style={{fontSize:14,fontWeight:600,color:T.gray10,margin:"0 0 8px",textTransform:"uppercase"}}>POURQUOI EST-CE IMPORTANT ?</h3><p style={{fontSize:14,color:T.gray10,lineHeight:1.7,margin:0,maxWidth:680}}>Ce poste a accès au dossier patient en Pédiatrie. Propagation au réseau hospitalier possible.</p></div>
    <div style={{borderTop:`1px solid ${T.gray90}`,paddingTop:16}}><h3 style={{fontSize:14,fontWeight:600,color:T.gray10,margin:"0 0 8px",textTransform:"uppercase"}}>ACTIONS RECOMMANDÉES</h3><ol style={{margin:0,paddingLeft:18,fontSize:13,lineHeight:2,color:T.gray10}}><li>Isoler immédiatement le poste</li><li>Identifier l'utilisateur connecté</li><li>Investiguer powershell.exe</li><li>Vérifier les sauvegardes</li></ol></div>
  </Card>
</Shell>}

// Admin: Alert Detail — status=acknowledged (AC3.2.5: [Close][Mark FP])
function Admin_AlertDetail_Ack(){return<Shell role="admin" nav="alerts" notif={2}>
  <div style={{display:"flex",alignItems:"center",gap:6,marginBottom:16,color:T.blue40,fontSize:13,cursor:"pointer"}}><ChevronLeft size={14}/>Retour</div>
  <div style={{display:"flex",alignItems:"center",gap:10,marginBottom:16}}><Score n={92}/><SevBadge l="critical"/><StatBadge s="acknowledged"/><span style={{fontSize:12,color:T.gray60}}>· Assigné à Marc Tchoumi</span></div>
  <Card style={{background:T.blue95,borderLeft:`4px solid ${T.blue40}`,marginBottom:20}}>
    <p style={{fontSize:13,color:T.gray10,margin:"0 0 10px"}}>Alerte prise en charge par Marc Tchoumi il y a 8 min</p>
    <div style={{display:"flex",gap:8}}><Btn v="primary">Fermer l'incident</Btn><Btn v="secondary">Marquer faux positif</Btn><Btn v="danger"><ShieldOff size={14}/>Isoler</Btn></div>
    <p style={{fontSize:10,color:T.gray60,margin:"6px 0 0",fontStyle:"italic"}}>AC3.2.5: status=acknowledged → [Close][Mark FP]. AC4.3.1: admin → [Isolate]</p>
  </Card>
</Shell>}

// Admin: Alert Detail — status=closed (AC3.4.3: read-only)
function Admin_AlertDetail_Closed(){return<Shell role="admin" nav="alerts" notif={2}>
  <div style={{display:"flex",alignItems:"center",gap:6,marginBottom:16,color:T.blue40,fontSize:13,cursor:"pointer"}}><ChevronLeft size={14}/>Retour</div>
  <div style={{display:"flex",alignItems:"center",gap:10,marginBottom:16}}><Score n={92}/><SevBadge l="critical"/><StatBadge s="closed"/></div>
  <Card style={{background:T.green95,borderLeft:`4px solid ${T.green50}`,marginBottom:20}}>
    <p style={{fontSize:14,fontWeight:600,color:T.green40,margin:"0 0 8px"}}>Incident fermé</p>
    <p style={{fontSize:13,color:T.gray10,margin:0}}><strong>Catégorie:</strong> Vrai positif — Contenu</p>
    <p style={{fontSize:13,color:T.gray10,margin:"4px 0"}}><strong>Résolution:</strong> Ransomware LockBit 3.0 détecté et contenu. Endpoint isolé, nettoyé, et restauré depuis backup.</p>
    <p style={{fontSize:12,color:T.gray60,margin:"4px 0 0"}}>Fermé par Marc Tchoumi · il y a 2h</p>
    <p style={{fontSize:10,color:T.gray60,margin:"6px 0 0",fontStyle:"italic"}}>AC3.4.3: closed → aucune action. Résolution affichée.</p>
  </Card>
</Shell>}

// Admin: Modal — Acknowledge (AC3.3.1)
function Admin_Modal_Ack(){return<Shell role="admin" nav="alerts" notif={2}>
  <div style={{display:"flex",justifyContent:"center",paddingTop:20}}>
    <div style={{width:"100%",maxWidth:480,background:T.white,borderRadius:12,boxShadow:"0 20px 25px rgba(15,17,21,.1)",overflow:"hidden"}}>
      <div style={{padding:20,borderBottom:`1px solid ${T.gray90}`}}><h2 style={{fontSize:18,fontWeight:600,color:T.gray10,margin:0}}>Prendre en charge cette alerte ?</h2><p style={{fontSize:13,color:T.gray40,margin:"6px 0 0"}}>Vous allez vous assigner l'alerte #a1f3 (CRITIQUE — PEDIATRIE-02)</p></div>
      <div style={{padding:20}}>
        <label style={{fontSize:13,fontWeight:500,color:T.gray40}}>Note (facultative, 0-500 caractères)</label>
        <textarea placeholder="Investigation en cours..." style={{width:"100%",minHeight:60,padding:10,borderRadius:6,border:`1px solid ${T.gray80}`,fontSize:13,marginTop:4,resize:"vertical",boxSizing:"border-box"}}/>
        <p style={{fontSize:10,color:T.gray60,margin:"6px 0 0",fontStyle:"italic"}}>AC3.3.1: note facultative 0-500 chars</p>
      </div>
      <div style={{background:T.gray95,padding:"10px 20px",display:"flex",justifyContent:"flex-end",gap:8}}><Btn v="secondary">Annuler</Btn><Btn v="primary">Confirmer</Btn></div>
    </div>
  </div>
</Shell>}

// Admin: Modal — Close Alert (AC3.4.1)
function Admin_Modal_Close(){return<Shell role="admin" nav="alerts" notif={2}>
  <div style={{display:"flex",justifyContent:"center",paddingTop:20}}>
    <div style={{width:"100%",maxWidth:480,background:T.white,borderRadius:12,boxShadow:"0 20px 25px rgba(15,17,21,.1)",overflow:"hidden"}}>
      <div style={{padding:20,borderBottom:`1px solid ${T.gray90}`}}><h2 style={{fontSize:18,fontWeight:600,color:T.gray10,margin:0}}>Fermer l'incident</h2></div>
      <div style={{padding:20}}>
        <label style={{fontSize:13,fontWeight:500,color:T.gray40}}>Catégorie de résolution *</label>
        <select style={{width:"100%",padding:"8px 12px",borderRadius:6,border:`1px solid ${T.gray80}`,fontSize:13,marginTop:4,marginBottom:12}}>
          <option>Sélectionner...</option>
          <option>Faux positif</option>
          <option>Vrai positif — Contenu</option>
          <option>Vrai positif — Escaladé</option>
          <option>Non concluant</option>
        </select>
        <label style={{fontSize:13,fontWeight:500,color:T.gray40}}>Notes de résolution * (20-2000 caractères)</label>
        <textarea placeholder="Détail de l'investigation et des actions prises..." style={{width:"100%",minHeight:80,padding:10,borderRadius:6,border:`1px solid ${T.gray80}`,fontSize:13,marginTop:4,resize:"vertical",boxSizing:"border-box"}}/>
        <div style={{textAlign:"right",fontSize:11,color:T.gray60,marginTop:4}}>0 / 2000</div>
        <p style={{fontSize:10,color:T.gray60,margin:"6px 0 0",fontStyle:"italic"}}>AC3.4.1: catégorie dropdown + notes 20-2000 chars requis</p>
      </div>
      <div style={{background:T.gray95,padding:"10px 20px",display:"flex",justifyContent:"flex-end",gap:8}}><Btn v="secondary">Annuler</Btn><Btn v="primary" disabled>Fermer l'incident</Btn></div>
    </div>
  </div>
</Shell>}

// Admin: Modal — Isolate (AC4.3.1)
function Admin_Modal_Isolate(){const[r,setR]=useState("");const[ck,setCk]=useState(false);return<Shell role="admin" nav="agents" notif={2}>
  <div style={{display:"flex",justifyContent:"center",paddingTop:10}}>
    <div style={{width:"100%",maxWidth:520,background:T.white,borderRadius:12,boxShadow:"0 20px 25px rgba(15,17,21,.1)",overflow:"hidden"}}>
      <div style={{background:T.red95,borderBottom:`1px solid ${T.red40}30`,padding:20,display:"flex",gap:14,alignItems:"flex-start"}}>
        <div style={{width:44,height:44,borderRadius:22,background:T.red50,display:"flex",alignItems:"center",justifyContent:"center",flexShrink:0}}><ShieldOff size={22} color={T.white}/></div>
        <h2 style={{fontSize:18,fontWeight:600,color:T.gray10,margin:0}}>Isoler PEDIATRIE-02 du réseau ?</h2>
      </div>
      <div style={{padding:20}}>
        <p style={{fontSize:13,color:T.gray10,lineHeight:1.7,margin:"0 0 16px"}}>Cette action va déconnecter PEDIATRIE-02 de tous les services réseau, à l'exception de la console RansomGuard. L'utilisateur perdra l'accès SIH, EMR, PACS.</p>
        <label style={{fontSize:13,fontWeight:500,color:T.gray40}}>Raison * (20-500 caractères)</label>
        <textarea value={r} onChange={e=>setR(e.target.value)} placeholder="Ex: Activité ransomware confirmée..." style={{width:"100%",minHeight:70,padding:10,borderRadius:6,border:`1px solid ${T.gray80}`,fontSize:13,marginTop:4,resize:"vertical",boxSizing:"border-box"}}/>
        <div style={{textAlign:"right",fontSize:11,color:r.length<20?T.orange50:T.gray60,marginTop:4}}>{r.length} / 500</div>
        <div style={{display:"flex",alignItems:"center",gap:8,marginTop:10}}><input type="checkbox" checked={ck} onChange={()=>setCk(!ck)} style={{accentColor:T.blue40}}/><span style={{fontSize:12,color:T.gray10}}>Je comprends que cela va déconnecter le poste</span></div>
      </div>
      <div style={{background:T.gray95,padding:"10px 20px",display:"flex",justifyContent:"flex-end",gap:8}}><Btn v="secondary">Annuler</Btn><Btn v="danger" disabled={r.length<20||!ck}><ShieldOff size={14}/>Isoler le poste</Btn></div>
    </div>
  </div>
</Shell>}

// Admin: Modal — Add User (US5.2 AC5.2.1)
function Admin_Modal_AddUser(){return<Shell role="admin" nav="users" notif={2}>
  <div style={{display:"flex",justifyContent:"center",paddingTop:10}}>
    <div style={{width:"100%",maxWidth:480,background:T.white,borderRadius:12,boxShadow:"0 20px 25px rgba(15,17,21,.1)",overflow:"hidden"}}>
      <div style={{padding:20,borderBottom:`1px solid ${T.gray90}`}}><h2 style={{fontSize:18,fontWeight:600,color:T.gray10,margin:0}}>Ajouter un utilisateur</h2></div>
      <div style={{padding:20,display:"flex",flexDirection:"column",gap:12}}>
        <Inp label="Nom complet *" placeholder="Jean Ndjock"/>
        <Inp label="Email *" placeholder="jean@hopital-yde.cm" type="email"/>
        <div><label style={{fontSize:13,fontWeight:500,color:T.gray40}}>Rôle *</label>
          <select style={{width:"100%",padding:"8px 12px",borderRadius:6,border:`1px solid ${T.gray80}`,fontSize:13,marginTop:4}}>
            <option>Sélectionner un rôle...</option>
            <option>Administrateur Hôpital (tenant_admin)</option>
            <option>Analyste Sécurité (security_analyst)</option>
            <option>Auditeur Lecture seule (read_only_auditor)</option>
          </select>
          <p style={{fontSize:10,color:T.gray60,margin:"4px 0",fontStyle:"italic"}}>AC5.2.4: seulement 3 rôles disponibles</p>
        </div>
        <Inp label="Mot de passe initial *" placeholder="••••••••••••" type="password"/>
        <div style={{display:"flex",alignItems:"center",gap:6,marginTop:-6}}>
          <div style={{flex:1,height:6,background:T.gray90,borderRadius:3}}><div style={{width:"70%",height:"100%",background:T.orange50,borderRadius:3}}/></div>
          <span style={{fontSize:11,color:T.orange50}}>Moyen</span>
        </div>
        <p style={{fontSize:10,color:T.gray60,margin:0}}>≥12 car., majuscule, chiffre, symbole</p>
        <Inp label="Confirmer le mot de passe *" placeholder="••••••••••••" type="password"/>
      </div>
      <div style={{background:T.gray95,padding:"10px 20px",display:"flex",justifyContent:"flex-end",gap:8}}><Btn v="secondary">Annuler</Btn><Btn v="primary">Créer l'utilisateur</Btn></div>
    </div>
  </div>
</Shell>}

// Admin: Agent Detail (AC4.2.1 + AC4.3.1 isolate visible)
function Admin_AgentDetail(){return<Shell role="admin" nav="agents" notif={2}>
  <div style={{display:"flex",alignItems:"center",gap:6,marginBottom:16,color:T.blue40,fontSize:13,cursor:"pointer"}}><ChevronLeft size={14}/>Retour aux agents</div>
  <div style={{display:"flex",alignItems:"center",gap:12,marginBottom:16}}>
    <h2 style={{fontSize:20,fontWeight:600,color:T.gray10,margin:0,fontFamily:"monospace"}}>PEDIATRIE-02</h2><StatBadge s="new"/><Dot c={T.green50}/><span style={{fontSize:12,color:T.gray60}}>Actif · Heartbeat il y a 12s</span>
  </div>
  <div style={{display:"grid",gridTemplateColumns:"repeat(3,1fr)",gap:12,marginBottom:16}}>
    <Card><span style={{fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>OS</span><p style={{margin:"4px 0 0",fontSize:14,color:T.gray10}}>Windows 10 Pro 22H2</p></Card>
    <Card><span style={{fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>VERSION AGENT</span><p style={{margin:"4px 0 0",fontSize:14,color:T.gray10,fontFamily:"monospace"}}>v0.7.0</p></Card>
    <Card><span style={{fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>INSCRIT LE</span><p style={{margin:"4px 0 0",fontSize:14,color:T.gray10}}>15 avril 2026</p></Card>
  </div>
  {/* AC4.3.1: admin → Isolate button visible */}
  <div style={{marginBottom:20}}><Btn v="danger"><ShieldOff size={14}/>Isoler le poste du réseau</Btn><p style={{fontSize:10,color:T.gray60,margin:"4px 0 0",fontStyle:"italic"}}>AC4.3.1: visible car rôle = tenant_admin</p></div>
  {/* AC4.2.2: Tabs */}
  <div style={{display:"flex",borderBottom:`1px solid ${T.gray90}`,marginBottom:16}}>{["Vue d'ensemble","Alertes (3)","Heartbeats","Configuration"].map((t,i)=><span key={t} style={{padding:"8px 14px",fontSize:13,fontWeight:i===0?500:400,color:i===0?T.blue40:T.gray40,borderBottom:i===0?`2px solid ${T.blue40}`:"none",cursor:"pointer"}}>{t}</span>)}</div>
  <Card><p style={{fontSize:13,color:T.gray40}}>Vue d'ensemble de l'agent avec alertes récentes et configuration active.</p></Card>
</Shell>}

// ═══════════════════════════════════════════════════
// RÔLE 2: SECURITY_ANALYST (Marc Tchoumi)
// IT: 9/10, Cybersec: 6/10, Fréquence: 8h/jour
// Quote: "Donnez-moi les bonnes infos, laissez-moi faire"
// ═══════════════════════════════════════════════════

function Analyst_Dashboard(){return<Shell role="analyst" nav="dashboard" notif={3}>
  <h1 style={{fontSize:22,fontWeight:600,color:T.gray10,margin:0}}>Console opérationnelle</h1>
  <p style={{fontSize:12,color:T.gray60,margin:"4px 0 20px"}}>⌘K pour la palette de commandes</p>
  {/* AC2.3.1: Agent health summary */}
  <Card style={{marginBottom:20}}>
    <div style={{display:"flex",gap:40}}>{[{n:42,l:"actifs",c:T.green50},{n:3,l:"obsolètes",c:T.yellow50},{n:2,l:"hors ligne",c:T.red50}].map(a=><div key={a.l} style={{display:"flex",alignItems:"center",gap:10}}><Dot c={a.c}/><span style={{fontSize:22,fontWeight:700,color:T.gray10}}>{a.n}</span><span style={{fontSize:12,color:T.gray60}}>{a.l}</span></div>)}</div>
  </Card>
  {/* AC4.4.3: Stale agent warning */}
  <div style={{background:T.orange95,border:`1px solid ${T.orange40}30`,borderRadius:8,padding:"8px 16px",marginBottom:16,display:"flex",alignItems:"center",gap:8}}><AlertTriangle size={16} color={T.orange50}/><span style={{fontSize:12,color:T.gray10}}>2 agents n'ont pas envoyé de heartbeat depuis plus d'1 heure</span></div>
  {/* AC2.3.2: Alert feed 15s refresh */}
  <Card style={{marginBottom:20}}>
    <div style={{display:"flex",justifyContent:"space-between",marginBottom:10}}><h3 style={{fontSize:14,fontWeight:600,color:T.gray10,margin:0}}>FLUX D'ALERTES</h3><span style={{fontSize:11,color:T.gray60,display:"flex",alignItems:"center",gap:4}}><Dot c={T.green50}/>Toutes les 15s</span></div>
    <div style={{display:"flex",gap:6,marginBottom:12}}>{["Tous","Sévérité ▼","1h ▼","Module ▼"].map(f=><span key={f} style={{padding:"3px 10px",borderRadius:4,border:`1px solid ${T.gray80}`,fontSize:11,color:T.gray40,cursor:"pointer"}}>{f}</span>)}</div>
    {ALERTS.map((a,i)=><div key={a.id} style={{display:"flex",alignItems:"center",gap:10,padding:"10px 0",borderTop:i?`1px solid ${T.gray90}`:"none"}}>
      <Score n={a.score}/><SevBadge l={a.sev}/><div style={{flex:1}}><span style={{fontFamily:"monospace",fontSize:12,fontWeight:600,color:T.gray10}}>{a.host}</span><span style={{fontSize:10,color:T.gray60,marginLeft:6,textTransform:"uppercase"}}>{a.module}</span><p style={{fontSize:12,color:T.gray40,margin:"2px 0 0"}}>{a.summary}</p></div>
      <span style={{fontSize:11,color:T.gray60}}>{a.age}</span>
      <Btn v="secondary" sz="sm">Prendre en charge</Btn><Btn v="ghost" sz="sm">Voir →</Btn>
    </div>)}
  </Card>
  {/* AC2.3.3: Agent table preview (10 agents) */}
  <Card>
    <div style={{display:"flex",justifyContent:"space-between",marginBottom:10}}><h3 style={{fontSize:14,fontWeight:600,color:T.gray10,margin:0}}>AGENTS</h3><span style={{fontSize:12,color:T.blue40,cursor:"pointer"}}>Voir tout →</span></div>
    <table style={{width:"100%",borderCollapse:"collapse",fontSize:13}}>
      <thead><tr style={{borderBottom:`1px solid ${T.gray90}`}}>{["Hôte","OS","Heartbeat","État"].map(h=><th key={h} style={{textAlign:"left",padding:"6px 10px",fontSize:11,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>{h}</th>)}</tr></thead>
      <tbody>{AGENTS.map(a=><tr key={a.host} style={{borderBottom:`1px solid ${T.gray90}`}}><td style={{padding:"6px 10px",fontFamily:"monospace",fontWeight:600,color:T.gray10,fontSize:12}}>{a.host}</td><td style={{padding:"6px 10px",color:T.gray40}}>{a.os}</td><td style={{padding:"6px 10px",color:T.gray60}}>{a.hb}</td><td style={{padding:"6px 10px"}}><span style={{display:"flex",alignItems:"center",gap:4}}><Dot c={{online:T.green50,stale:T.yellow50,offline:T.red50}[a.st]}/>{{online:"Actif",stale:"Obsolète",offline:"Hors ligne"}[a.st]}</span></td></tr>)}</tbody>
    </table>
  </Card>
</Shell>}

// Analyst: Alert Detail — NO Isolate button (AC4.3.2)
function Analyst_AlertDetail(){return<Shell role="analyst" nav="alerts" notif={3}>
  <div style={{display:"flex",alignItems:"center",gap:6,marginBottom:16,color:T.blue40,fontSize:13}}><ChevronLeft size={14}/>Retour</div>
  <div style={{display:"flex",alignItems:"center",gap:10,marginBottom:16}}><Score n={92}/><SevBadge l="critical"/><StatBadge s="new"/></div>
  <Card style={{background:T.orange95,borderLeft:`4px solid ${T.orange50}`,marginBottom:20}}>
    <div style={{display:"flex",gap:8,marginBottom:8}}><Btn v="primary">Prendre en charge</Btn><Btn v="secondary">Marquer faux positif</Btn></div>
    <p style={{fontSize:10,color:T.red50,margin:0,fontWeight:600}}>⚠ Bouton "Isoler" NON VISIBLE — rôle = security_analyst (AC4.3.2: analyst → 403)</p>
    <p style={{fontSize:10,color:T.gray60,margin:"4px 0 0"}}>Pour isoler un endpoint, contactez un administrateur hôpital.</p>
  </Card>
  <div style={{display:"flex",borderBottom:`1px solid ${T.gray90}`,marginBottom:16}}>{["Résumé","Chronologie","Artefacts","Actions"].map((t,i)=><span key={t} style={{padding:"8px 14px",fontSize:13,color:i===0?T.blue40:T.gray40,borderBottom:i===0?`2px solid ${T.blue40}`:"none",cursor:"pointer"}}>{t}</span>)}</div>
  <Card><p style={{fontSize:13,color:T.gray10,lineHeight:1.7}}>Narrative inverted pyramid identique à la vue admin. Seul le bouton Isoler est absent.</p></Card>
</Shell>}

// Analyst: /users → 403 (AC5.1.2)
function Analyst_Forbidden(){return<Shell role="analyst" nav="dashboard" notif={3}>
  <div style={{display:"flex",flexDirection:"column",alignItems:"center",justifyContent:"center",minHeight:300,gap:12}}>
    <ShieldOff size={40} color={T.red50}/>
    <h1 style={{fontSize:18,fontWeight:600,color:T.gray10}}>Accès refusé</h1>
    <p style={{fontSize:13,color:T.gray40,textAlign:"center",maxWidth:300}}>Vous n'avez pas les autorisations nécessaires pour accéder à cette page.</p>
    <p style={{fontSize:10,color:T.gray60,fontStyle:"italic"}}>AC5.1.2: security_analyst → /users redirigé + 403</p>
  </div>
</Shell>}

// ═══════════════════════════════════════════════════
// RÔLE 3: READ_ONLY_AUDITOR (Sr. Jeanne Bilo'o)
// IT: 4/10, Cybersec: 2/10, Fréquence: 1-2x/mois
// Quote: "Données fiables et vérifiables"
// ═══════════════════════════════════════════════════

function Auditor_Dashboard(){return<Shell role="auditor" nav="dashboard" notif={0}>
  <h1 style={{fontSize:22,fontWeight:600,color:T.gray10,margin:0}}>Vue de conformité</h1>
  <p style={{fontSize:13,color:T.gray40,margin:"4px 0 20px"}}>Hôpital Régional de Yaoundé · Accès en lecture seule</p>
  {/* AC2.4.1: Audit-focused content */}
  <Card style={{background:T.green95,borderLeft:`4px solid ${T.green50}`,marginBottom:20}}>
    <p style={{fontSize:16,fontWeight:600,color:T.green40,margin:0}}>✅ CONFORMITÉ ACTUELLE : 98%</p>
    <p style={{fontSize:13,color:T.gray10,margin:"6px 0 0"}}>Loi 2024/017 : Conforme</p>
    <p style={{fontSize:12,color:T.gray40,margin:"4px 0 0"}}>Prochain audit ANTIC : 15 septembre 2026</p>
  </Card>
  <div style={{display:"grid",gridTemplateColumns:"1fr 1fr",gap:12,marginBottom:20}}>
    <KPI label="Alertes dernières 24h" value="12" sub="dont 3 critiques"/>
    <KPI label="Journal derniers 7 jours" value="487" sub="entrées d'audit"/>
  </div>
  {/* AC2.4.1: Two prominent buttons */}
  <div style={{display:"flex",flexDirection:"column",gap:12,marginBottom:20}}>
    <Card style={{cursor:"pointer"}}><div style={{display:"flex",justifyContent:"space-between",alignItems:"center"}}><div><p style={{fontSize:14,fontWeight:600,color:T.gray10,margin:0}}>📜 Consulter le journal d'audit</p><p style={{fontSize:12,color:T.gray40,margin:"3px 0 0"}}>Recherche, filtres, export CSV</p></div><span style={{color:T.blue40,fontSize:12}}>Accéder →</span></div></Card>
    <Card style={{cursor:"pointer"}}><div style={{display:"flex",justifyContent:"space-between",alignItems:"center"}}><div><p style={{fontSize:14,fontWeight:600,color:T.gray10,margin:0}}>🚨 Consulter les alertes</p><p style={{fontSize:12,color:T.gray40,margin:"3px 0 0"}}>Lecture seule</p></div><span style={{color:T.blue40,fontSize:12}}>Voir →</span></div></Card>
  </div>
  {/* AC2.4.2: No write actions */}
  <div style={{background:T.blue95,borderRadius:8,padding:14,display:"flex",gap:10,alignItems:"flex-start"}}>
    <Info size={16} color={T.blue40} style={{marginTop:2,flexShrink:0}}/>
    <p style={{fontSize:12,color:T.gray40,margin:0}}>Vous êtes connectée en mode auditeur. Aucune modification possible. AC2.4.2: no write actions visible.</p>
  </div>
</Shell>}

// Auditor: Alert Detail — NO action buttons (AC3.2.5 + AC3.3.3)
function Auditor_AlertDetail(){return<Shell role="auditor" nav="alerts" notif={0}>
  <div style={{display:"flex",alignItems:"center",gap:6,marginBottom:16,color:T.blue40,fontSize:13}}><ChevronLeft size={14}/>Retour</div>
  <div style={{display:"flex",alignItems:"center",gap:10,marginBottom:16}}><Score n={92}/><SevBadge l="critical"/><StatBadge s="new"/></div>
  {/* AC3.2.5: read_only_auditor → no action buttons */}
  <div style={{background:T.blue95,borderRadius:8,padding:14,marginBottom:20,display:"flex",gap:10,alignItems:"center"}}>
    <Info size={16} color={T.blue40}/>
    <span style={{fontSize:13,color:T.gray40}}>Mode lecture seule — aucune action disponible</span>
  </div>
  <div style={{display:"flex",borderBottom:`1px solid ${T.gray90}`,marginBottom:16}}>{["Résumé","Chronologie","Artefacts","Actions"].map((t,i)=><span key={t} style={{padding:"8px 14px",fontSize:13,color:i===0?T.blue40:T.gray40,borderBottom:i===0?`2px solid ${T.blue40}`:"none",cursor:"pointer"}}>{t}</span>)}</div>
  <Card><p style={{fontSize:13,color:T.gray10,lineHeight:1.7}}>Narrative identique. Aucun bouton d'action. AC3.3.3: auditor cannot acknowledge (403 API-side).</p></Card>
</Shell>}

// Auditor: Alerts List — only "Voir" per AC3.1.5
function Auditor_AlertsList(){return<Shell role="auditor" nav="alerts" notif={0}>
  <h1 style={{fontSize:22,fontWeight:600,color:T.gray10,margin:"0 0 16px"}}>Alertes</h1>
  <Card style={{padding:0,overflow:"hidden"}}>
    <table style={{width:"100%",borderCollapse:"collapse",fontSize:13}}>
      <thead><tr style={{background:T.gray95}}>{["Score","Sévérité","Hôte","Module","Statut",""].map(h=><th key={h} style={{textAlign:"left",padding:"8px 10px",fontSize:11,fontWeight:600,color:T.gray40,textTransform:"uppercase",borderBottom:`1px solid ${T.gray80}`}}>{h}</th>)}</tr></thead>
      <tbody>{ALERTS.map(a=><tr key={a.id} style={{borderBottom:`1px solid ${T.gray90}`}}>
        <td style={{padding:"8px 10px"}}><Score n={a.score}/></td>
        <td style={{padding:"8px 10px"}}><SevBadge l={a.sev}/></td>
        <td style={{padding:"8px 10px",fontFamily:"monospace",fontWeight:600,fontSize:12}}>{a.host}</td>
        <td style={{padding:"8px 10px",fontSize:11,color:T.gray40,textTransform:"uppercase"}}>{a.module}</td>
        <td style={{padding:"8px 10px"}}><StatBadge s="new"/></td>
        <td style={{padding:"8px 10px"}}><Btn v="ghost" sz="sm">Voir →</Btn></td>
      </tr>)}</tbody>
    </table>
  </Card>
  <p style={{fontSize:10,color:T.gray60,margin:"8px 0",fontStyle:"italic"}}>AC3.1.5: read_only_auditor → no row actions except "View details"</p>
</Shell>}

// Empty State (AC3.1.3)
function EmptyState(){return<Shell role="analyst" nav="alerts" notif={0}>
  <h1 style={{fontSize:22,fontWeight:600,color:T.gray10,margin:"0 0 16px"}}>Alertes</h1>
  <div style={{display:"flex",gap:6,marginBottom:16}}><span style={{padding:"3px 10px",borderRadius:4,border:`1px solid ${T.gray80}`,fontSize:11,color:T.gray40,background:T.blue95}}>Statut: Résolu ×</span></div>
  <Card style={{display:"flex",flexDirection:"column",alignItems:"center",justifyContent:"center",padding:48,gap:12}}>
    <Shield size={36} color={T.gray60}/>
    <p style={{fontSize:15,fontWeight:600,color:T.gray10}}>Aucune alerte ne correspond à vos filtres</p>
    <p style={{fontSize:13,color:T.gray40,textAlign:"center"}}>Essayez d'élargir votre recherche ou de réinitialiser les filtres.</p>
    <Btn v="secondary">Réinitialiser les filtres</Btn>
    <p style={{fontSize:10,color:T.gray60,fontStyle:"italic"}}>AC3.1.3: empty state per IA §Empty States</p>
  </Card>
</Shell>}

// ═══════════════════════════════════════════════════
// SHARED: LOGIN (US1.1)
// ═══════════════════════════════════════════════════
function Shared_Login(){return<div style={{display:"flex",alignItems:"center",justifyContent:"center",height:"100%",background:T.gray95,padding:16,fontFamily:"system-ui,-apple-system,sans-serif"}}>
  <Card style={{maxWidth:380,width:"100%"}}>
    <div style={{textAlign:"center",marginBottom:20}}><p style={{fontSize:16,fontWeight:600,color:T.gray10,margin:0}}>🛡 RansomGuard-CM</p><p style={{fontSize:13,color:T.gray40,margin:"3px 0 0"}}>Console de sécurité</p></div>
    <h2 style={{fontSize:18,fontWeight:600,color:T.gray10,textAlign:"center",margin:"0 0 16px"}}>Connexion</h2>
    <div style={{display:"flex",flexDirection:"column",gap:14}}>
      <Inp label="Email" placeholder="marc.tchoumi@hopital-yde.cm" type="email"/>
      <Inp label="Mot de passe" placeholder="••••••••" type="password"/>
      <div style={{display:"flex",alignItems:"center",gap:6}}><input type="checkbox" style={{accentColor:T.blue40}}/><span style={{fontSize:12,color:T.gray40}}>Se souvenir de moi</span></div>
      <Btn v="primary" full sz="lg">Se connecter</Btn>
    </div>
    <p style={{fontSize:10,color:T.gray60,margin:"12px 0 0",fontStyle:"italic"}}>AC1.1.1-6: email+password → POST /auth/login → JWT in-memory (AC1.1.6) + redirect role-based (AC2.1)</p>
    <div style={{display:"flex",justifyContent:"space-between",alignItems:"center",marginTop:16,paddingTop:12,borderTop:`1px solid ${T.gray90}`,fontSize:11,color:T.gray60}}>
      <span>v0.9.0</span><span style={{display:"flex",alignItems:"center",gap:3,cursor:"pointer"}}><Globe size={12}/>FR ▼</span>
    </div>
  </Card>
</div>}

function Shared_Login_Error(){return<div style={{display:"flex",alignItems:"center",justifyContent:"center",height:"100%",background:T.gray95,padding:16,fontFamily:"system-ui,-apple-system,sans-serif"}}>
  <Card style={{maxWidth:380,width:"100%"}}>
    <div style={{textAlign:"center",marginBottom:20}}><p style={{fontSize:16,fontWeight:600,color:T.gray10,margin:0}}>🛡 RansomGuard-CM</p></div>
    <h2 style={{fontSize:18,fontWeight:600,color:T.gray10,textAlign:"center",margin:"0 0 16px"}}>Connexion</h2>
    <div style={{display:"flex",flexDirection:"column",gap:14}}>
      <Inp label="Email" placeholder="marc.tchoumi@hopital-yde.cm" type="email"/>
      <Inp label="Mot de passe" placeholder="••••••••" type="password"/>
      {/* AC1.1.2: Invalid credentials error inline */}
      <div style={{background:T.red95,border:`1px solid ${T.red40}30`,borderRadius:6,padding:"8px 12px",display:"flex",alignItems:"center",gap:8}}>
        <AlertCircle size={14} color={T.red50}/>
        <span style={{fontSize:12,color:T.red50}}>Email ou mot de passe incorrect</span>
      </div>
      <Btn v="primary" full sz="lg">Se connecter</Btn>
    </div>
    <p style={{fontSize:10,color:T.gray60,margin:"10px 0 0",fontStyle:"italic"}}>AC1.1.2: 401 → inline error "Email ou mot de passe incorrect" + shake animation 200ms</p>
  </Card>
</div>}

// ═══════════════════════════════════════════════════
// SHARED: SKELETON LOADING (IA §Loading States)
// ═══════════════════════════════════════════════════
function Shared_Skeleton(){
  const Sk=({w,h=14})=><div style={{width:w,height:h,borderRadius:4,background:T.gray90,animation:"pulse 1.5s ease-in-out infinite"}}/>;
  return<Shell role="analyst" nav="dashboard" notif={0}>
    <Sk w="200px" h={24}/>
    <div style={{marginTop:20,display:"grid",gridTemplateColumns:"repeat(3,1fr)",gap:12}}>
      {[1,2,3].map(i=><Card key={i}><Sk w="80px" h={10}/><div style={{marginTop:10}}><Sk w="60px" h={28}/></div><div style={{marginTop:8}}><Sk w="120px" h={10}/></div></Card>)}
    </div>
    <Card style={{marginTop:20}}>
      <Sk w="160px" h={16}/>
      {[1,2,3,4].map(i=><div key={i} style={{display:"flex",gap:12,alignItems:"center",padding:"12px 0",borderTop:i>1?`1px solid ${T.gray90}`:"none"}}><Sk w="36px" h={20}/><Sk w="80px" h={16}/><Sk w="120px" h={14}/><div style={{flex:1}}/><Sk w="60px" h={12}/></div>)}
    </Card>
    <p style={{fontSize:10,color:T.gray60,margin:"12px 0",fontStyle:"italic"}}>IA §Loading States: skeleton screens remplacent les données réelles pendant le chargement. Pas de spinner plein écran — le layout est toujours visible.</p>
  </Shell>
}

// ═══════════════════════════════════════════════════
// SHARED: TOAST NOTIFICATIONS (IA §Error States)
// ═══════════════════════════════════════════════════
function Shared_Toasts(){return<Shell role="analyst" nav="agents" notif={3}>
  <h1 style={{fontSize:22,fontWeight:600,color:T.gray10,margin:"0 0 20px"}}>Exemples de toasts</h1>
  <div style={{display:"flex",flexDirection:"column",gap:12}}>
    {/* Success toast — AC3.3.4 optimistic + AC4.3.1 command queued */}
    <div style={{background:T.white,borderLeft:`4px solid ${T.green50}`,borderRadius:6,padding:"12px 16px",boxShadow:"0 10px 15px rgba(15,17,21,.1)",display:"flex",alignItems:"center",gap:10}}>
      <Check size={18} color={T.green50}/>
      <div><p style={{fontSize:13,fontWeight:600,color:T.gray10,margin:0}}>Alerte prise en charge</p><p style={{fontSize:12,color:T.gray40,margin:"2px 0 0"}}>Vous êtes assigné à l'alerte #a1f3</p></div>
      <X size={14} color={T.gray60} style={{marginLeft:"auto",cursor:"pointer"}}/>
    </div>
    {/* Warning toast — AC4.3.1 command queued */}
    <div style={{background:T.white,borderLeft:`4px solid ${T.orange50}`,borderRadius:6,padding:"12px 16px",boxShadow:"0 10px 15px rgba(15,17,21,.1)",display:"flex",alignItems:"center",gap:10}}>
      <Clock size={18} color={T.orange50}/>
      <div><p style={{fontSize:13,fontWeight:600,color:T.gray10,margin:0}}>Commande envoyée</p><p style={{fontSize:12,color:T.gray40,margin:"2px 0 0"}}>L'agent sera contacté au prochain heartbeat</p></div>
      <X size={14} color={T.gray60} style={{marginLeft:"auto",cursor:"pointer"}}/>
    </div>
    {/* Error toast — network/server */}
    <div style={{background:T.white,borderLeft:`4px solid ${T.red50}`,borderRadius:6,padding:"12px 16px",boxShadow:"0 10px 15px rgba(15,17,21,.1)",display:"flex",alignItems:"center",gap:10}}>
      <AlertCircle size={18} color={T.red50}/>
      <div><p style={{fontSize:13,fontWeight:600,color:T.gray10,margin:0}}>Erreur serveur</p><p style={{fontSize:12,color:T.gray40,margin:"2px 0 0"}}>Veuillez réessayer dans quelques instants</p></div>
      <Btn v="ghost" sz="sm">Réessayer</Btn>
    </div>
    {/* Info toast — session expiry */}
    <div style={{background:T.white,borderLeft:`4px solid ${T.blue50}`,borderRadius:6,padding:"12px 16px",boxShadow:"0 10px 15px rgba(15,17,21,.1)",display:"flex",alignItems:"center",gap:10}}>
      <Info size={18} color={T.blue50}/>
      <div><p style={{fontSize:13,fontWeight:600,color:T.gray10,margin:0}}>Session bientôt expirée</p><p style={{fontSize:12,color:T.gray40,margin:"2px 0 0"}}>Votre session expire dans 5 minutes</p></div>
    </div>
  </div>
  <p style={{fontSize:10,color:T.gray60,margin:"16px 0",fontStyle:"italic"}}>Position: en haut à droite, z-index toast (500). Auto-dismiss 4s sauf erreur. AC3.3.4: optimistic revert shows error toast.</p>
</Shell>}

// ═══════════════════════════════════════════════════
// ADMIN: Alerts List (AC3.1.1-5)
// ═══════════════════════════════════════════════════
function Admin_AlertsList(){return<Shell role="admin" nav="alerts" notif={2}>
  <h1 style={{fontSize:22,fontWeight:600,color:T.gray10,margin:"0 0 16px"}}>Alertes</h1>
  <div style={{position:"relative",marginBottom:12}}><Search size={14} color={T.gray60} style={{position:"absolute",left:10,top:9}}/><input placeholder="Rechercher (ID, hôte, résumé...)" style={{width:"100%",padding:"7px 10px 7px 32px",borderRadius:6,border:`1px solid ${T.gray80}`,fontSize:13,boxSizing:"border-box"}}/></div>
  <div style={{display:"flex",gap:6,marginBottom:12}}>{["Sévérité ▼","Statut: Nouveau ×","Module ▼","24h ▼"].map(f=><span key={f} style={{padding:"3px 10px",borderRadius:4,border:`1px solid ${T.gray80}`,fontSize:11,color:T.gray40,cursor:"pointer",background:f.includes("×")?T.blue95:"transparent"}}>{f}</span>)}<span style={{fontSize:11,color:T.blue40,cursor:"pointer",padding:"3px 6px"}}>Réinitialiser</span></div>
  <div style={{display:"flex",marginBottom:12}}>{[{l:"File (47)",a:true},{l:"Mes assignées (5)",a:false},{l:"Résolues",a:false}].map(t=><span key={t.l} style={{padding:"7px 14px",fontSize:13,fontWeight:t.a?500:400,color:t.a?T.blue40:T.gray40,borderBottom:t.a?`2px solid ${T.blue40}`:`2px solid transparent`,cursor:"pointer"}}>{t.l}</span>)}</div>
  <Card style={{padding:0,overflow:"hidden"}}>
    <table style={{width:"100%",borderCollapse:"collapse",fontSize:13}}>
      <thead><tr style={{background:T.gray95}}>{["Score","Sévérité","Hôte","Module","Statut","Détecté","Actions"].map(h=><th key={h} style={{textAlign:"left",padding:"8px 10px",fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase",borderBottom:`1px solid ${T.gray80}`}}>{h}</th>)}</tr></thead>
      <tbody>{ALERTS.map(a=><tr key={a.id} style={{borderBottom:`1px solid ${T.gray90}`}}>
        <td style={{padding:"8px 10px"}}><Score n={a.score}/></td>
        <td style={{padding:"8px 10px"}}><SevBadge l={a.sev}/></td>
        <td style={{padding:"8px 10px",fontFamily:"monospace",fontWeight:600,fontSize:12}}>{a.host}</td>
        <td style={{padding:"8px 10px",fontSize:10,color:T.gray40,textTransform:"uppercase"}}>{a.module}</td>
        <td style={{padding:"8px 10px"}}><StatBadge s="new"/></td>
        <td style={{padding:"8px 10px",fontSize:11,color:T.gray60}}>{a.age}</td>
        <td style={{padding:"8px 10px"}}><Btn v="secondary" sz="sm">Prendre en charge</Btn></td>
      </tr>)}</tbody>
    </table>
  </Card>
  <div style={{display:"flex",justifyContent:"flex-end",gap:10,marginTop:12,fontSize:12,color:T.gray40}}>← Précédent <strong>Page 1/3</strong> Suivant →</div>
  <p style={{fontSize:10,color:T.gray60,margin:"8px 0",fontStyle:"italic"}}>AC3.1.1: sortable by priority score desc. AC3.1.4: admin/analyst see action buttons. AC3.1.2: filters multi-select per IA §FilterBar.</p>
</Shell>}

// ═══════════════════════════════════════════════════
// ADMIN: Users Management (US5.1-5.4)
// ═══════════════════════════════════════════════════
function Admin_UsersList(){
  const users=[
    {ini:"AN",name:"Dr. Amani Nkomo",email:"amani@hopital-yde.cm",role:"Admin",st:"active",self:true},
    {ini:"MT",name:"Marc Tchoumi",email:"marc@hopital-yde.cm",role:"Analyste",st:"active"},
    {ini:"JN",name:"Jean Ndjock",email:"jean@hopital-yde.cm",role:"Analyste",st:"active"},
    {ini:"JB",name:"Sr Jeanne Bilo'o",email:"jeanne@minsante.cm",role:"Auditeur",st:"active"},
    {ini:"PK",name:"Paul Kamga",email:"paul@hopital-yde.cm",role:"Analyste",st:"disabled"},
  ];
  return<Shell role="admin" nav="users" notif={2}>
    <div style={{display:"flex",justifyContent:"space-between",alignItems:"center",marginBottom:16}}>
      <h1 style={{fontSize:22,fontWeight:600,color:T.gray10,margin:0}}>Gestion des utilisateurs</h1>
      <Btn v="primary">+ Ajouter un utilisateur</Btn>
    </div>
    <div style={{display:"flex",gap:8,marginBottom:12}}>
      <div style={{flex:1,position:"relative"}}><Search size={14} color={T.gray60} style={{position:"absolute",left:10,top:9}}/><input placeholder="Rechercher par nom ou email" style={{width:"100%",padding:"7px 10px 7px 32px",borderRadius:6,border:`1px solid ${T.gray80}`,fontSize:13,boxSizing:"border-box"}}/></div>
      {["Statut: Tous ▼","Rôle: Tous ▼"].map(f=><span key={f} style={{padding:"7px 10px",borderRadius:6,border:`1px solid ${T.gray80}`,fontSize:11,color:T.gray40,cursor:"pointer"}}>{f}</span>)}
    </div>
    <Card style={{padding:0,overflow:"hidden"}}>
      <table style={{width:"100%",borderCollapse:"collapse",fontSize:13}}>
        <thead><tr style={{background:T.gray95}}>{["","Nom","Email","Rôle","Statut",""].map(h=><th key={h} style={{textAlign:"left",padding:"8px 10px",fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase",borderBottom:`1px solid ${T.gray80}`}}>{h}</th>)}</tr></thead>
        <tbody>{users.map(u=><tr key={u.email} style={{borderBottom:`1px solid ${T.gray90}`,opacity:u.st==="disabled"?.6:1}}>
          <td style={{padding:"8px 10px"}}><div style={{width:28,height:28,borderRadius:14,background:T.blue40,display:"flex",alignItems:"center",justifyContent:"center",fontSize:10,fontWeight:700,color:T.white}}>{u.ini}</div></td>
          <td style={{padding:"8px 10px",fontWeight:500,color:T.gray10}}>{u.name}{u.self&&<span style={{fontSize:10,color:T.gray60}}> (vous)</span>}</td>
          <td style={{padding:"8px 10px",color:T.gray40,fontSize:12}}>{u.email}</td>
          <td style={{padding:"8px 10px"}}><Badge>{u.role}</Badge></td>
          <td style={{padding:"8px 10px"}}><span style={{display:"flex",alignItems:"center",gap:4}}><Dot c={u.st==="active"?T.green50:T.gray60}/>{u.st==="active"?"Actif":"Désactivé"}</span></td>
          <td style={{padding:"8px 10px",textAlign:"right",color:T.gray40,cursor:"pointer"}}>⋮</td>
        </tr>)}</tbody>
      </table>
    </Card>
    <div style={{fontSize:12,color:T.gray40,marginTop:10}}>5 utilisateurs · AC5.1.1-2: admin only page · AC5.3.2: cannot self-disable · AC5.4.2: cannot self-change role</div>
    <div style={{background:T.blue95,borderRadius:6,padding:12,marginTop:12,display:"flex",gap:8,alignItems:"center"}}><Info size={14} color={T.blue40}/><span style={{fontSize:12,color:T.gray40}}>Vous ne pouvez pas modifier votre propre rôle ni désactiver votre propre compte.</span></div>
  </Shell>
}

// ═══════════════════════════════════════════════════
// ADMIN: Audit Log (US6.1-6.3)
// ═══════════════════════════════════════════════════
function Admin_AuditLog(){
  const entries=[
    {ts:"08/06 06:42",action:"user_enabled",actor:"amani@",target:"Paul Kamga",ip:"10.0.1.45"},
    {ts:"08/06 06:38",action:"alert_acknowledged",actor:"marc@",target:"Alert #a1f3",ip:"10.0.1.22"},
    {ts:"08/06 06:35",action:"endpoint_isolated",actor:"amani@",target:"PEDIATRIE-02",ip:"10.0.1.45"},
    {ts:"08/06 06:12",action:"login",actor:"marc@",target:"—",ip:"10.0.1.22"},
    {ts:"07/06 18:30",action:"user_disabled",actor:"amani@",target:"Paul Kamga",ip:"10.0.1.45"},
  ];
  return<Shell role="admin" nav="audit" notif={2}>
    <h1 style={{fontSize:22,fontWeight:600,color:T.gray10,margin:"0 0 16px"}}>Journal d'audit</h1>
    <div style={{display:"flex",gap:8,marginBottom:12}}>
      <div style={{flex:1,position:"relative"}}><Search size={14} color={T.gray60} style={{position:"absolute",left:10,top:9}}/><input placeholder="Rechercher..." style={{width:"100%",padding:"7px 10px 7px 32px",borderRadius:6,border:`1px solid ${T.gray80}`,fontSize:13,boxSizing:"border-box"}}/></div>
      <Btn v="secondary"><Download size={14}/>Exporter CSV</Btn>
    </div>
    <div style={{display:"flex",gap:6,marginBottom:12}}>{["7 jours ▼","Action ▼","Utilisateur ▼"].map(f=><span key={f} style={{padding:"3px 10px",borderRadius:4,border:`1px solid ${T.gray80}`,fontSize:11,color:T.gray40,cursor:"pointer"}}>{f}</span>)}</div>
    <Card style={{padding:0,overflow:"hidden"}}>
      <table style={{width:"100%",borderCollapse:"collapse",fontSize:13}}>
        <thead><tr style={{background:T.gray95}}>{["Horodatage","Action","Acteur","Cible","IP"].map(h=><th key={h} style={{textAlign:"left",padding:"8px 10px",fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase",borderBottom:`1px solid ${T.gray80}`}}>{h}</th>)}</tr></thead>
        <tbody>{entries.map((e,i)=><tr key={i} style={{borderBottom:`1px solid ${T.gray90}`}}>
          <td style={{padding:"8px 10px",fontFamily:"monospace",fontSize:11,color:T.gray60}}>{e.ts}</td>
          <td style={{padding:"8px 10px"}}><Badge>{e.action}</Badge></td>
          <td style={{padding:"8px 10px",color:T.gray40,fontSize:12}}>{e.actor}</td>
          <td style={{padding:"8px 10px",color:T.gray10}}>{e.target}</td>
          <td style={{padding:"8px 10px",fontFamily:"monospace",fontSize:11,color:T.gray60}}>{e.ip}</td>
        </tr>)}</tbody>
      </table>
    </Card>
    <p style={{fontSize:10,color:T.gray60,margin:"8px 0",fontStyle:"italic"}}>AC6.1.1: full-text search. AC6.2.1: date/action/user filters. AC6.3.1: CSV export. Signed Ed25519.</p>
  </Shell>
}

// ═══════════════════════════════════════════════════
// ADMIN: Agent Isolated + Restore (AC4.3.3-4)
// ═══════════════════════════════════════════════════
function Admin_AgentIsolated(){return<Shell role="admin" nav="agents" notif={2}>
  <div style={{display:"flex",alignItems:"center",gap:6,marginBottom:16,color:T.blue40,fontSize:13,cursor:"pointer"}}><ChevronLeft size={14}/>Retour</div>
  <div style={{display:"flex",alignItems:"center",gap:12,marginBottom:16}}>
    <h2 style={{fontSize:20,fontWeight:600,color:T.gray10,margin:0,fontFamily:"monospace"}}>PEDIATRIE-02</h2>
    <StatBadge s="isolated"/>
  </div>
  {/* AC4.3.4: most recent command status */}
  <Card style={{background:T.red95,borderLeft:`4px solid ${T.red50}`,marginBottom:16}}>
    <div style={{display:"flex",alignItems:"center",gap:8}}><ShieldOff size={18} color={T.red50}/><div>
      <p style={{fontSize:14,fontWeight:600,color:T.red50,margin:0}}>Endpoint isolé du réseau</p>
      <p style={{fontSize:12,color:T.gray40,margin:"4px 0 0"}}>Isolé par Dr. Amani Nkomo · il y a 32 min</p>
      <p style={{fontSize:12,color:T.gray40,margin:"2px 0 0"}}>Raison: Activité ransomware confirmée par SENTINEL</p>
    </div></div>
    {/* AC4.3.3: Restore button — admin only */}
    <div style={{marginTop:12}}><Btn v="primary"><Shield size={14}/>Restaurer l'accès réseau</Btn></div>
    <p style={{fontSize:10,color:T.gray60,margin:"6px 0 0",fontStyle:"italic"}}>AC4.3.3: Restore visible car rôle = tenant_admin. Confirmation modal similaire à isolate.</p>
  </Card>
  <div style={{display:"grid",gridTemplateColumns:"repeat(3,1fr)",gap:12,marginBottom:16}}>
    <Card><span style={{fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>ÉTAT</span><p style={{margin:"4px 0 0",fontSize:14,color:T.red50,fontWeight:600}}>🔴 Isolé</p></Card>
    <Card><span style={{fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>DERNIER HEARTBEAT</span><p style={{margin:"4px 0 0",fontSize:14,color:T.gray10}}>il y a 8 s</p></Card>
    <Card><span style={{fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>COMMANDE</span><p style={{margin:"4px 0 0",fontSize:14,color:T.green50}}>✅ Exécutée</p></Card>
  </div>
</Shell>}

function Admin_AgentPending(){return<Shell role="admin" nav="agents" notif={2}>
  <div style={{display:"flex",alignItems:"center",gap:6,marginBottom:16,color:T.blue40,fontSize:13,cursor:"pointer"}}><ChevronLeft size={14}/>Retour</div>
  <div style={{display:"flex",alignItems:"center",gap:12,marginBottom:16}}>
    <h2 style={{fontSize:20,fontWeight:600,color:T.gray10,margin:0,fontFamily:"monospace"}}>PEDIATRIE-02</h2>
    <StatBadge s="isolation_pending"/>
  </div>
  <Card style={{background:T.orange95,borderLeft:`4px solid ${T.orange50}`,marginBottom:16}}>
    <div style={{display:"flex",alignItems:"center",gap:8}}><Clock size={18} color={T.orange50}/><div>
      <p style={{fontSize:14,fontWeight:600,color:T.orange50,margin:0}}>Isolation en cours — en attente du prochain heartbeat</p>
      <p style={{fontSize:12,color:T.gray40,margin:"4px 0 0"}}>Commande envoyée il y a 15 s · Heartbeat attendu dans ~15 s</p>
    </div></div>
  </Card>
  <p style={{fontSize:10,color:T.gray60,fontStyle:"italic"}}>AC4.3.4: command status = pending. Polling 30s pour détecter le passage à "executed".</p>
</Shell>}

// ═══════════════════════════════════════════════════
// ANALYST: Alerts List (AC3.1.4 analyst actions)
// ═══════════════════════════════════════════════════
function Analyst_AlertsList(){return<Shell role="analyst" nav="alerts" notif={3}>
  <h1 style={{fontSize:22,fontWeight:600,color:T.gray10,margin:"0 0 16px"}}>Alertes</h1>
  <div style={{position:"relative",marginBottom:12}}><Search size={14} color={T.gray60} style={{position:"absolute",left:10,top:9}}/><input placeholder="Rechercher..." style={{width:"100%",padding:"7px 10px 7px 32px",borderRadius:6,border:`1px solid ${T.gray80}`,fontSize:13,boxSizing:"border-box"}}/></div>
  <div style={{display:"flex",gap:6,marginBottom:12}}>{["Sévérité ▼","Statut ▼","Module ▼","Période ▼"].map(f=><span key={f} style={{padding:"3px 10px",borderRadius:4,border:`1px solid ${T.gray80}`,fontSize:11,color:T.gray40,cursor:"pointer"}}>{f}</span>)}</div>
  <Card style={{padding:0,overflow:"hidden"}}>
    <table style={{width:"100%",borderCollapse:"collapse",fontSize:13}}>
      <thead><tr style={{background:T.gray95}}>{["Score","Sévérité","Hôte","Module","Statut","Détecté","Actions"].map(h=><th key={h} style={{textAlign:"left",padding:"8px 10px",fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase",borderBottom:`1px solid ${T.gray80}`}}>{h}</th>)}</tr></thead>
      <tbody>{ALERTS.map(a=><tr key={a.id} style={{borderBottom:`1px solid ${T.gray90}`}}>
        <td style={{padding:"8px 10px"}}><Score n={a.score}/></td>
        <td style={{padding:"8px 10px"}}><SevBadge l={a.sev}/></td>
        <td style={{padding:"8px 10px",fontFamily:"monospace",fontWeight:600,fontSize:12}}>{a.host}</td>
        <td style={{padding:"8px 10px",fontSize:10,color:T.gray40,textTransform:"uppercase"}}>{a.module}</td>
        <td style={{padding:"8px 10px"}}><StatBadge s="new"/></td>
        <td style={{padding:"8px 10px",fontSize:11,color:T.gray60}}>{a.age}</td>
        <td style={{padding:"8px 10px"}}><div style={{display:"flex",gap:4}}><Btn v="secondary" sz="sm">Prendre en charge</Btn><Btn v="ghost" sz="sm">Voir →</Btn></div></td>
      </tr>)}</tbody>
    </table>
  </Card>
  <p style={{fontSize:10,color:T.gray60,margin:"8px 0",fontStyle:"italic"}}>AC3.1.4: analyst voit [Acknowledge] + [View]. PAS de bouton [Isoler] (AC4.3.2). Identique à admin sauf isolation.</p>
</Shell>}

// Analyst: Agents List — View only (AC4.3.2)
function Analyst_AgentsList(){return<Shell role="analyst" nav="agents" notif={3}>
  <h1 style={{fontSize:22,fontWeight:600,color:T.gray10,margin:"0 0 16px"}}>Agents</h1>
  <Card style={{padding:0,overflow:"hidden"}}>
    <table style={{width:"100%",borderCollapse:"collapse",fontSize:13}}>
      <thead><tr style={{background:T.gray95}}>{["Hôte","OS","Version","Heartbeat","État",""].map(h=><th key={h} style={{textAlign:"left",padding:"8px 10px",fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase",borderBottom:`1px solid ${T.gray80}`}}>{h}</th>)}</tr></thead>
      <tbody>{AGENTS.map(a=><tr key={a.host} style={{borderBottom:`1px solid ${T.gray90}`}}>
        <td style={{padding:"8px 10px",fontFamily:"monospace",fontWeight:600,fontSize:12}}>{a.host}</td>
        <td style={{padding:"8px 10px",color:T.gray40}}>{a.os}</td>
        <td style={{padding:"8px 10px",fontFamily:"monospace",fontSize:11,color:T.gray60}}>{a.ver}</td>
        <td style={{padding:"8px 10px",color:T.gray60}}>il y a {a.hb}</td>
        <td style={{padding:"8px 10px"}}><span style={{display:"flex",alignItems:"center",gap:4}}><Dot c={{online:T.green50,stale:T.yellow50,offline:T.red50}[a.st]}/>{{online:"Actif",stale:"Obsolète",offline:"Hors ligne"}[a.st]}</span></td>
        <td style={{padding:"8px 10px"}}><Btn v="ghost" sz="sm">Voir →</Btn></td>
      </tr>)}</tbody>
    </table>
  </Card>
  <p style={{fontSize:10,color:T.gray60,margin:"8px 0",fontStyle:"italic"}}>AC4.3.2: analyst → PAS de menu "Isoler" ni "Send command". View only. Admin uniquement pour les commandes.</p>
</Shell>}

// Analyst: Agent Detail — NO Isolate (AC4.3.2)
function Analyst_AgentDetail(){return<Shell role="analyst" nav="agents" notif={3}>
  <div style={{display:"flex",alignItems:"center",gap:6,marginBottom:16,color:T.blue40,fontSize:13,cursor:"pointer"}}><ChevronLeft size={14}/>Retour</div>
  <div style={{display:"flex",alignItems:"center",gap:12,marginBottom:16}}>
    <h2 style={{fontSize:20,fontWeight:600,color:T.gray10,margin:0,fontFamily:"monospace"}}>PEDIATRIE-02</h2>
    <Dot c={T.green50}/><span style={{fontSize:12,color:T.gray60}}>Actif</span>
  </div>
  <div style={{display:"grid",gridTemplateColumns:"repeat(3,1fr)",gap:12,marginBottom:16}}>
    <Card><span style={{fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>OS</span><p style={{margin:"4px 0 0",fontSize:14}}>Windows 10 Pro</p></Card>
    <Card><span style={{fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>VERSION</span><p style={{margin:"4px 0 0",fontSize:14,fontFamily:"monospace"}}>v0.7.0</p></Card>
    <Card><span style={{fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>HEARTBEAT</span><p style={{margin:"4px 0 0",fontSize:14}}>il y a 12s</p></Card>
  </div>
  {/* AC4.3.2: NO Isolate button */}
  <div style={{background:T.blue95,borderRadius:6,padding:12,display:"flex",gap:8,alignItems:"center",marginBottom:16}}>
    <Info size={14} color={T.blue40}/>
    <span style={{fontSize:12,color:T.gray40}}>Les commandes d'isolation sont réservées aux administrateurs hôpital.</span>
  </div>
  <p style={{fontSize:10,color:T.red50,fontWeight:600,fontStyle:"italic"}}>AC4.3.2: security_analyst → aucun bouton de commande. Seul tenant_admin peut isoler/restaurer.</p>
  <div style={{display:"flex",borderBottom:`1px solid ${T.gray90}`,marginBottom:16,marginTop:12}}>{["Vue d'ensemble","Alertes (3)","Heartbeats","Configuration"].map((t,i)=><span key={t} style={{padding:"8px 14px",fontSize:13,color:i===0?T.blue40:T.gray40,borderBottom:i===0?`2px solid ${T.blue40}`:"none",cursor:"pointer"}}>{t}</span>)}</div>
  <Card><p style={{fontSize:13,color:T.gray40}}>Vue d'ensemble de l'agent.</p></Card>
</Shell>}

// Analyst: /audit → 403 (AC6.1.2)
function Analyst_AuditForbidden(){return<Shell role="analyst" nav="dashboard" notif={3}>
  <div style={{display:"flex",flexDirection:"column",alignItems:"center",justifyContent:"center",minHeight:300,gap:12}}>
    <ShieldOff size={40} color={T.red50}/>
    <h1 style={{fontSize:18,fontWeight:600,color:T.gray10}}>Accès refusé</h1>
    <p style={{fontSize:13,color:T.gray40,textAlign:"center",maxWidth:300}}>Le journal d'audit est réservé aux administrateurs hôpital et aux auditeurs.</p>
    <p style={{fontSize:10,color:T.gray60,fontStyle:"italic"}}>AC6.1.2: security_analyst → /audit redirigé + 403</p>
  </div>
</Shell>}

// Analyst: Alert Acknowledged variant (AC3.2.5)
function Analyst_AlertAck(){return<Shell role="analyst" nav="alerts" notif={3}>
  <div style={{display:"flex",alignItems:"center",gap:6,marginBottom:16,color:T.blue40,fontSize:13}}><ChevronLeft size={14}/>Retour</div>
  <div style={{display:"flex",alignItems:"center",gap:10,marginBottom:16}}><Score n={92}/><SevBadge l="critical"/><StatBadge s="acknowledged"/><span style={{fontSize:12,color:T.gray60}}>· Assigné à vous</span></div>
  <Card style={{background:T.blue95,borderLeft:`4px solid ${T.blue40}`,marginBottom:20}}>
    <p style={{fontSize:13,color:T.gray10,margin:"0 0 10px"}}>Vous avez pris en charge cette alerte il y a 12 min</p>
    <div style={{display:"flex",gap:8}}><Btn v="primary">Fermer l'incident</Btn><Btn v="secondary">Marquer faux positif</Btn></div>
    <p style={{fontSize:10,color:T.red50,margin:"8px 0 0",fontWeight:600}}>⚠ PAS de bouton Isoler — rôle analyst (AC4.3.2)</p>
  </Card>
</Shell>}

// ═══════════════════════════════════════════════════
// AUDITOR: Agents List (view only)
// ═══════════════════════════════════════════════════
function Auditor_AgentsList(){return<Shell role="auditor" nav="agents" notif={0}>
  <h1 style={{fontSize:22,fontWeight:600,color:T.gray10,margin:"0 0 16px"}}>Agents</h1>
  <Card style={{padding:0,overflow:"hidden"}}>
    <table style={{width:"100%",borderCollapse:"collapse",fontSize:13}}>
      <thead><tr style={{background:T.gray95}}>{["Hôte","OS","Heartbeat","État"].map(h=><th key={h} style={{textAlign:"left",padding:"8px 10px",fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase",borderBottom:`1px solid ${T.gray80}`}}>{h}</th>)}</tr></thead>
      <tbody>{AGENTS.map(a=><tr key={a.host} style={{borderBottom:`1px solid ${T.gray90}`}}>
        <td style={{padding:"8px 10px",fontFamily:"monospace",fontWeight:600,fontSize:12}}>{a.host}</td>
        <td style={{padding:"8px 10px",color:T.gray40}}>{a.os}</td>
        <td style={{padding:"8px 10px",color:T.gray60}}>il y a {a.hb}</td>
        <td style={{padding:"8px 10px"}}><span style={{display:"flex",alignItems:"center",gap:4}}><Dot c={{online:T.green50,stale:T.yellow50,offline:T.red50}[a.st]}/>{{online:"Actif",stale:"Obsolète",offline:"Hors ligne"}[a.st]}</span></td>
      </tr>)}</tbody>
    </table>
  </Card>
  <div style={{background:T.blue95,borderRadius:6,padding:10,marginTop:12,display:"flex",gap:8,alignItems:"center"}}><Info size={14} color={T.blue40}/><span style={{fontSize:11,color:T.gray40}}>Mode lecture seule. Aucune action disponible.</span></div>
</Shell>}

// Auditor: Audit Log (read + export — US6.1-6.3)
function Auditor_AuditLog(){
  const entries=[
    {ts:"08/06 06:42",action:"user_enabled",actor:"amani@",target:"Paul Kamga"},
    {ts:"08/06 06:35",action:"endpoint_isolated",actor:"amani@",target:"PEDIATRIE-02"},
    {ts:"08/06 06:12",action:"login",actor:"marc@",target:"—"},
    {ts:"07/06 18:30",action:"user_disabled",actor:"amani@",target:"Paul Kamga"},
  ];
  return<Shell role="auditor" nav="audit" notif={0}>
    <h1 style={{fontSize:22,fontWeight:600,color:T.gray10,margin:"0 0 16px"}}>Journal d'audit</h1>
    <div style={{display:"flex",gap:8,marginBottom:12}}>
      <div style={{flex:1,position:"relative"}}><Search size={14} color={T.gray60} style={{position:"absolute",left:10,top:9}}/><input placeholder="Rechercher..." style={{width:"100%",padding:"7px 10px 7px 32px",borderRadius:6,border:`1px solid ${T.gray80}`,fontSize:13,boxSizing:"border-box"}}/></div>
      <Btn v="secondary"><Download size={14}/>Exporter CSV</Btn>
    </div>
    <Card style={{padding:0,overflow:"hidden"}}>
      <table style={{width:"100%",borderCollapse:"collapse",fontSize:13}}>
        <thead><tr style={{background:T.gray95}}>{["Horodatage","Action","Acteur","Cible"].map(h=><th key={h} style={{textAlign:"left",padding:"8px 10px",fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase",borderBottom:`1px solid ${T.gray80}`}}>{h}</th>)}</tr></thead>
        <tbody>{entries.map((e,i)=><tr key={i} style={{borderBottom:`1px solid ${T.gray90}`}}>
          <td style={{padding:"8px 10px",fontFamily:"monospace",fontSize:11,color:T.gray60}}>{e.ts}</td>
          <td style={{padding:"8px 10px"}}><Badge>{e.action}</Badge></td>
          <td style={{padding:"8px 10px",color:T.gray40,fontSize:12}}>{e.actor}</td>
          <td style={{padding:"8px 10px",color:T.gray10}}>{e.target}</td>
        </tr>)}</tbody>
      </table>
    </Card>
    <p style={{fontSize:10,color:T.gray60,margin:"8px 0",fontStyle:"italic"}}>AC6.1.1: full-text search. AC6.3.1: CSV export. Auditor: read + export only, no modifications.</p>
  </Shell>
}

// Auditor: Agent Detail — no actions
function Auditor_AgentDetail(){return<Shell role="auditor" nav="agents" notif={0}>
  <div style={{display:"flex",alignItems:"center",gap:6,marginBottom:16,color:T.blue40,fontSize:13,cursor:"pointer"}}><ChevronLeft size={14}/>Retour</div>
  <div style={{display:"flex",alignItems:"center",gap:12,marginBottom:16}}>
    <h2 style={{fontSize:20,fontWeight:600,color:T.gray10,margin:0,fontFamily:"monospace"}}>PEDIATRIE-02</h2>
    <Dot c={T.green50}/><span style={{fontSize:12,color:T.gray60}}>Actif</span>
  </div>
  <div style={{display:"grid",gridTemplateColumns:"repeat(3,1fr)",gap:12,marginBottom:16}}>
    <Card><span style={{fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>OS</span><p style={{margin:"4px 0 0",fontSize:14}}>Windows 10 Pro</p></Card>
    <Card><span style={{fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>VERSION</span><p style={{margin:"4px 0 0",fontSize:14,fontFamily:"monospace"}}>v0.7.0</p></Card>
    <Card><span style={{fontSize:10,fontWeight:600,color:T.gray40,textTransform:"uppercase"}}>HEARTBEAT</span><p style={{margin:"4px 0 0",fontSize:14}}>il y a 12s</p></Card>
  </div>
  <div style={{background:T.blue95,borderRadius:6,padding:12,display:"flex",gap:8,alignItems:"center"}}><Info size={14} color={T.blue40}/><span style={{fontSize:12,color:T.gray40}}>Mode lecture seule. Aucune action disponible.</span></div>
</Shell>}

// Auditor: /users → 403
function Auditor_UsersForbidden(){return<Shell role="auditor" nav="dashboard" notif={0}>
  <div style={{display:"flex",flexDirection:"column",alignItems:"center",justifyContent:"center",minHeight:300,gap:12}}>
    <ShieldOff size={40} color={T.red50}/>
    <h1 style={{fontSize:18,fontWeight:600,color:T.gray10}}>Accès refusé</h1>
    <p style={{fontSize:13,color:T.gray40,textAlign:"center",maxWidth:300}}>La gestion des utilisateurs est réservée aux administrateurs hôpital.</p>
    <p style={{fontSize:10,color:T.gray60,fontStyle:"italic"}}>AC5.1.2: read_only_auditor → /users → 403</p>
  </div>
</Shell>}

// ═══════════════════════════════════════════════════
// MAIN NAVIGATOR — COMPLET
// ═══════════════════════════════════════════════════
const ALL_SCREENS = {
  shared: [
    {k:"s_login",l:"Login",c:Shared_Login,ac:"US1.1 AC1.1.1-6"},
    {k:"s_login_err",l:"Login — Erreur",c:Shared_Login_Error,ac:"AC1.1.2 invalid credentials"},
    {k:"s_skeleton",l:"Skeleton Loading",c:Shared_Skeleton,ac:"IA §Loading States"},
    {k:"s_toasts",l:"Toast Notifications",c:Shared_Toasts,ac:"IA §Error States + AC3.3.4"},
  ],
  admin: [
    {k:"a_dash",l:"Dashboard (normal)",c:Admin_Dashboard_Normal,ac:"AC2.2.1-6"},
    {k:"a_dash_ok",l:"Dashboard (tout OK)",c:Admin_Dashboard_AllClear,ac:"AC2.2.1 variant vert"},
    {k:"a_alerts",l:"Alerts List",c:Admin_AlertsList,ac:"AC3.1.1-5"},
    {k:"a_alert_new",l:"Alerte — Nouveau",c:Admin_AlertDetail_New,ac:"AC3.2.5 + AC4.3.1 Isolate"},
    {k:"a_alert_ack",l:"Alerte — En cours",c:Admin_AlertDetail_Ack,ac:"AC3.2.5 acknowledged"},
    {k:"a_alert_closed",l:"Alerte — Fermé",c:Admin_AlertDetail_Closed,ac:"AC3.4.3 read-only"},
    {k:"a_m_ack",l:"Modal: Acknowledge",c:Admin_Modal_Ack,ac:"AC3.3.1"},
    {k:"a_m_close",l:"Modal: Fermer",c:Admin_Modal_Close,ac:"AC3.4.1"},
    {k:"a_m_isolate",l:"Modal: Isoler",c:Admin_Modal_Isolate,ac:"AC4.3.1 admin only"},
    {k:"a_agent_det",l:"Agent Détail",c:Admin_AgentDetail,ac:"AC4.2.1-3 + AC4.3.1"},
    {k:"a_agent_iso",l:"Agent — Isolé",c:Admin_AgentIsolated,ac:"AC4.3.3-4 Restore"},
    {k:"a_agent_pend",l:"Agent — Pending",c:Admin_AgentPending,ac:"AC4.3.4 command pending"},
    {k:"a_users",l:"Users Management",c:Admin_UsersList,ac:"AC5.1-5.4"},
    {k:"a_m_adduser",l:"Modal: Ajouter User",c:Admin_Modal_AddUser,ac:"AC5.2.1-5"},
    {k:"a_audit",l:"Audit Log",c:Admin_AuditLog,ac:"AC6.1-6.3"},
  ],
  analyst: [
    {k:"n_dash",l:"Dashboard Opérationnel",c:Analyst_Dashboard,ac:"AC2.3.1-4 + AC4.4.3"},
    {k:"n_alerts",l:"Alerts List",c:Analyst_AlertsList,ac:"AC3.1.4 analyst actions"},
    {k:"n_alert_new",l:"Alerte — Nouveau (PAS Isoler)",c:Analyst_AlertDetail,ac:"AC4.3.2 no isolate"},
    {k:"n_alert_ack",l:"Alerte — Acknowledged",c:Analyst_AlertAck,ac:"AC3.2.5 + AC4.3.2"},
    {k:"n_agents",l:"Agents List (View only)",c:Analyst_AgentsList,ac:"AC4.3.2 no commands"},
    {k:"n_agent_det",l:"Agent Détail (NO Isolate)",c:Analyst_AgentDetail,ac:"AC4.3.2"},
    {k:"n_403_users",l:"/users → 403",c:Analyst_Forbidden,ac:"AC5.1.2"},
    {k:"n_403_audit",l:"/audit → 403",c:Analyst_AuditForbidden,ac:"AC6.1.2"},
    {k:"n_empty",l:"Alertes — État vide",c:EmptyState,ac:"AC3.1.3"},
  ],
  auditor: [
    {k:"j_dash",l:"Dashboard Conformité",c:Auditor_Dashboard,ac:"AC2.4.1-3"},
    {k:"j_alerts",l:"Alertes (View only)",c:Auditor_AlertsList,ac:"AC3.1.5 no actions"},
    {k:"j_alert_ro",l:"Alerte — Lecture seule",c:Auditor_AlertDetail,ac:"AC3.2.5 + AC3.3.3"},
    {k:"j_agents",l:"Agents (View only)",c:Auditor_AgentsList,ac:"AC4.3.2 no actions"},
    {k:"j_agent_det",l:"Agent Détail (no actions)",c:Auditor_AgentDetail,ac:"view only"},
    {k:"j_audit",l:"Audit Log (read + export)",c:Auditor_AuditLog,ac:"AC6.1-6.3"},
    {k:"j_403_users",l:"/users → 403",c:Auditor_UsersForbidden,ac:"AC5.1.2"},
  ],
};

export default function App(){
  const[role,setRole]=useState("shared");
  const screens=ALL_SCREENS[role]||[];
  const[screen,setScreen]=useState(screens[0]?.k);
  const cur=screens.find(s=>s.k===screen)||screens[0];
  const Comp=cur?.c||(()=><div/>);
  
  const handleRole=(r)=>{setRole(r);setScreen(ALL_SCREENS[r]?.[0]?.k)};
  
  const roleCfg=[
    {k:"shared",l:"🔗 Partagé (4)",c:"#5B6170"},
    {k:"admin",l:"👩‍⚕️ Admin — 15 écrans",c:"#3A66A6"},
    {k:"analyst",l:"🔧 Analyste — 9 écrans",c:"#D9842B"},
    {k:"auditor",l:"📋 Auditeur — 7 écrans",c:"#3AAF65"},
  ];
  const roleNames={shared:"Écrans partagés",admin:"tenant_admin",analyst:"security_analyst",auditor:"read_only_auditor"};
  const total=Object.values(ALL_SCREENS).reduce((s,a)=>s+a.length,0);
  
  return<div style={{height:"100vh",display:"flex",flexDirection:"column",fontFamily:"system-ui,-apple-system,sans-serif"}}>
    {/* Control bar */}
    <div style={{background:T.gray10,padding:"6px 12px",display:"flex",alignItems:"center",gap:10,flexShrink:0,flexWrap:"wrap"}}>
      <span style={{color:T.white,fontWeight:700,fontSize:13}}>🛡 MAQUETTES v2</span>
      <span style={{color:T.gray60,fontSize:10}}>|</span>
      <span style={{color:T.gray60,fontSize:10}}>{total} écrans total</span>
      <span style={{color:T.gray60,fontSize:10}}>|</span>
      {roleCfg.map(r=><button key={r.k} onClick={()=>handleRole(r.k)} style={{padding:"3px 8px",borderRadius:4,fontSize:10,fontWeight:role===r.k?700:400,color:role===r.k?T.white:T.gray60,background:role===r.k?r.c:"transparent",border:`1px solid ${role===r.k?r.c:T.gray60}40`,cursor:"pointer"}}>{r.l}</button>)}
    </div>
    {/* Screen tabs */}
    <div style={{background:T.gray20,padding:"0 4px",display:"flex",gap:0,overflow:"auto",flexShrink:0}}>
      {screens.map(s=><button key={s.k} onClick={()=>setScreen(s.k)} style={{padding:"5px 8px",fontSize:10,fontWeight:screen===s.k?600:400,color:screen===s.k?T.white:T.gray60,background:screen===s.k?"#3D424E":"transparent",border:"none",cursor:"pointer",whiteSpace:"nowrap",borderBottom:screen===s.k?`2px solid ${T.blue50}`:"2px solid transparent"}}>{s.l}</button>)}
    </div>
    {/* AC reference */}
    <div style={{background:T.yellow95,padding:"3px 12px",fontSize:10,color:T.yellow20,borderBottom:`1px solid ${T.yellow50}30`,flexShrink:0}}>
      <strong>AC:</strong> {cur?.ac} — <strong>Rôle:</strong> {roleNames[role]}
    </div>
    {/* Render */}
    <div style={{flex:1,overflow:"hidden"}}><Comp/></div>
  </div>
}
