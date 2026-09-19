import React, { useEffect, useState } from "react";
import {
  Building2,
  ChevronDown,
  ChevronRight,
  Copy,
  FolderTree,
  HardDrive,
  Info,
  KeyRound,
  Layers,
  Lock,
  Network,
  Plus,
  Radio,
  RefreshCw,
  Save,
  Search,
  Shield,
  ShieldAlert,
  ShieldCheck,
  Smartphone,
  Trash2,
  Usb,
  Zap,
} from "lucide-react";
import {
  applyProfilePolicyNow,
  cloneProfile,
  createProfile,
  deleteProfile,
  DeviceSummary,
  getProfile,
  getProfileTree,
  PolicyDocument,
  ProfileDetailResponse,
  ProfileTreeNode,
  ProfileUpsertRequest,
  updateProfile,
  UsbDeviceItem,
} from "../api";

interface ProfilesViewProps {
  devices: DeviceSummary[];
  onSelectDevice?: (deviceId: string) => void;
}

export const ProfilesView: React.FC<ProfilesViewProps> = ({ devices, onSelectDevice }) => {
  const [tree, setTree] = useState<ProfileTreeNode[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedProfileId, setSelectedProfileId] = useState<string | null>(null);
  const [profileDetail, setProfileDetail] = useState<ProfileDetailResponse | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");
  const [expandedNodes, setExpandedNodes] = useState<Set<string>>(new Set());

  // Form State (for edited profile)
  const [name, setName] = useState("");
  const [profileType, setProfileType] = useState("Company");
  const [activeTab, setActiveTab] = useState<"branding" | "protection" | "remote_access" | "usb" | "effective" | "devices">("branding");
  const [saving, setSaving] = useState(false);
  const [applying, setApplying] = useState(false);
  const [feedback, setFeedback] = useState<{ message: string; type: "success" | "error" } | null>(null);

  // Policy Form Sub-states
  const [policyDoc, setPolicyDoc] = useState<PolicyDocument | null>(null);
  const [newPassword, setNewPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);

  // Modal States
  const [createModalOpen, setCreateModalOpen] = useState(false);
  const [createParentId, setCreateParentId] = useState<string | null>(null);
  const [createName, setCreateName] = useState("");
  const [createType, setCreateType] = useState("Department");

  const [cloneModalOpen, setCloneModalOpen] = useState(false);
  const [cloneNewName, setCloneNewName] = useState("");

  const [deleteConfirmOpen, setDeleteConfirmOpen] = useState(false);

  // USB Whitelist modal
  const [usbModalOpen, setUsbModalOpen] = useState(false);
  const [usbName, setUsbName] = useState("");
  const [usbVendorId, setUsbVendorId] = useState("");
  const [usbProductId, setUsbProductId] = useState("");
  const [usbSerial, setUsbSerial] = useState("");

  const loadTree = async (selectId?: string) => {
    try {
      setLoading(true);
      const data = await getProfileTree();
      setTree(data);

      // Auto expand all root nodes
      const initialExpanded = new Set<string>();
      const addIds = (nodes: ProfileTreeNode[]) => {
        for (const n of nodes) {
          initialExpanded.add(n.id);
          if (n.children && n.children.length > 0) addIds(n.children);
        }
      };
      addIds(data);
      setExpandedNodes(initialExpanded);

      const targetId = selectId || selectedProfileId || (data.length > 0 ? data[0].id : null);
      if (targetId) {
        loadDetail(targetId);
      }
    } catch (err: any) {
      setFeedback({ message: err.message || "Profiller yüklenemedi.", type: "error" });
    } finally {
      setLoading(false);
    }
  };

  const loadDetail = async (id: string) => {
    try {
      setDetailLoading(true);
      setSelectedProfileId(id);
      const detail = await getProfile(id);
      setProfileDetail(detail);
      setName(detail.name);
      setProfileType(detail.type);
      setPolicyDoc(JSON.parse(JSON.stringify(detail.ownPolicy)));
      setNewPassword("");
    } catch (err: any) {
      setFeedback({ message: err.message || "Profil detayları alınamadı.", type: "error" });
    } finally {
      setDetailLoading(false);
    }
  };

  useEffect(() => {
    loadTree();
  }, []);

  const toggleExpand = (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    setExpandedNodes((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const showToast = (msg: string, type: "success" | "error" = "success") => {
    setFeedback({ message: msg, type });
    setTimeout(() => setFeedback(null), 5000);
  };

  const handleSave = async () => {
    if (!profileDetail || !policyDoc) return;
    if (!name.trim()) {
      showToast("Profil adı boş bırakılamaz.", "error");
      return;
    }

    try {
      setSaving(true);
      const req: ProfileUpsertRequest = {
        name: name.trim(),
        parentProfileId: profileDetail.parentProfileId,
        type: profileType,
        policy: policyDoc,
        newProtectionPassword: newPassword.trim() ? newPassword.trim() : null,
      };

      const updated = await updateProfile(profileDetail.id, req);
      setProfileDetail(updated);
      setPolicyDoc(JSON.parse(JSON.stringify(updated.ownPolicy)));
      setNewPassword("");
      showToast(`"${updated.name}" profili ve politikası başarıyla güncellendi (v${updated.policyVersion}).`);
      await loadTree(updated.id);
    } catch (err: any) {
      showToast(err.message || "Profil kaydedilemedi.", "error");
    } finally {
      setSaving(false);
    }
  };

  const handleApplyNow = async () => {
    if (!profileDetail) return;
    try {
      setApplying(true);
      const res = await applyProfilePolicyNow(profileDetail.id);
      showToast(res.message || "Politika tüm bağlı cihazlara uygulandı.");
    } catch (err: any) {
      showToast(err.message || "Politika anlık uygulama sinyali gönderilemedi.", "error");
    } finally {
      setApplying(false);
    }
  };

  const handleCreateSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!createName.trim()) return;

    try {
      const defaultDoc: PolicyDocument = {
        version: 1,
        branding: {},
        protection: {},
        remoteAccess: {},
        usb: {},
      };

      const created = await createProfile({
        name: createName.trim(),
        parentProfileId: createParentId,
        type: createType,
        policy: defaultDoc,
      });

      setCreateModalOpen(false);
      setCreateName("");
      showToast(`"${created.name}" profili başarıyla oluşturuldu.`);
      await loadTree(created.id);
    } catch (err: any) {
      showToast(err.message || "Profil oluşturulamadı.", "error");
    }
  };

  const handleCloneSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!profileDetail) return;
    try {
      const cloned = await cloneProfile(profileDetail.id, cloneNewName.trim() || undefined);
      setCloneModalOpen(false);
      setCloneNewName("");
      showToast(`"${cloned.name}" profili kopyalandı.`);
      await loadTree(cloned.id);
    } catch (err: any) {
      showToast(err.message || "Profil kopyalanamadı.", "error");
    }
  };

  const handleDeleteSubmit = async () => {
    if (!profileDetail) return;
    try {
      await deleteProfile(profileDetail.id);
      setDeleteConfirmOpen(false);
      showToast(`"${profileDetail.name}" profili silindi.`);
      setSelectedProfileId(null);
      setProfileDetail(null);
      await loadTree();
    } catch (err: any) {
      showToast(err.message || "Profil silinemedi.", "error");
      setDeleteConfirmOpen(false);
    }
  };

  const addUsbWhitelistItem = () => {
    if (!usbName.trim()) return;
    const item: UsbDeviceItem = {
      id: "usb-" + Math.random().toString(36).substring(2, 9),
      name: usbName.trim(),
      vendorId: usbVendorId.trim() || null,
      productId: usbProductId.trim() || null,
      serialNumber: usbSerial.trim() || null,
    };

    setPolicyDoc((prev) => {
      if (!prev) return prev;
      const currentList = prev.usb?.whitelist || [];
      return {
        ...prev,
        usb: {
          ...prev.usb,
          whitelist: [...currentList, item],
        },
      };
    });

    setUsbName("");
    setUsbVendorId("");
    setUsbProductId("");
    setUsbSerial("");
    setUsbModalOpen(false);
  };

  const removeUsbWhitelistItem = (itemId: string) => {
    setPolicyDoc((prev) => {
      if (!prev) return prev;
      const currentList = prev.usb?.whitelist || [];
      return {
        ...prev,
        usb: {
          ...prev.usb,
          whitelist: currentList.filter((i) => i.id !== itemId),
        },
      };
    });
  };

  // Helper renderers for tree
  const filterNode = (node: ProfileTreeNode): boolean => {
    if (!searchQuery) return true;
    const q = searchQuery.toLowerCase();
    if (node.name.toLowerCase().includes(q)) return true;
    if (node.companyName?.toLowerCase().includes(q)) return true;
    if (node.children && node.children.some(filterNode)) return true;
    return false;
  };

  const renderTreeNode = (node: ProfileTreeNode, level: number = 0) => {
    if (!filterNode(node)) return null;
    const hasChildren = node.children && node.children.length > 0;
    const isExpanded = expandedNodes.has(node.id);
    const isSelected = selectedProfileId === node.id;

    return (
      <div key={node.id} className="profile-tree-node-wrapper" style={{ marginLeft: `${level * 14}px` }}>
        <div
          className={`profile-tree-node ${isSelected ? "selected" : ""}`}
          onClick={() => loadDetail(node.id)}
        >
          <button
            type="button"
            className="profile-expand-toggle"
            onClick={(e) => hasChildren && toggleExpand(node.id, e)}
            style={{ visibility: hasChildren ? "visible" : "hidden" }}
          >
            {isExpanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
          </button>

          <span className="profile-node-icon">
            {node.type === "Company" && <Building2 size={16} className="text-primary" />}
            {node.type === "Department" && <Network size={16} className="text-emerald" />}
            {node.type === "Location" && <Layers size={16} className="text-amber" />}
            {node.type === "Custom" && <ShieldAlert size={16} className="text-purple" />}
          </span>

          <div className="profile-node-info">
            <span className="profile-node-name">{node.name}</span>
            <span className="profile-node-badges">
              <span className="badge badge-subtle">v{node.policyVersion}</span>
              {node.deviceCount > 0 && (
                <span className="badge badge-primary">{node.deviceCount} Cihaz</span>
              )}
            </span>
          </div>

          <div className="profile-node-hover-actions">
            <button
              type="button"
              className="btn-icon-xs"
              title="Alt Profil Ekle"
              onClick={(e) => {
                e.stopPropagation();
                setCreateParentId(node.id);
                setCreateType(node.type === "Company" ? "Department" : "Location");
                setCreateModalOpen(true);
              }}
            >
              <Plus size={13} />
            </button>
          </div>
        </div>

        {hasChildren && isExpanded && (
          <div className="profile-tree-children">
            {node.children.map((child) => renderTreeNode(child, level + 1))}
          </div>
        )}
      </div>
    );
  };

  const assignedDevices = profileDetail
    ? devices.filter((d) => d.profileId === profileDetail.id)
    : [];

  return (
    <div className="profiles-view-container">
      {/* Toast Feedback */}
      {feedback && (
        <div className={`profiles-toast ${feedback.type === "error" ? "error" : "success"}`}>
          {feedback.type === "error" ? <ShieldAlert size={16} /> : <ShieldCheck size={16} />}
          <span>{feedback.message}</span>
          <button type="button" onClick={() => setFeedback(null)} className="toast-close">
            ×
          </button>
        </div>
      )}

      {/* SOL PANEL: Hiyerarşik Profil Ağacı & Araç Çubuğu */}
      <div className="profiles-sidebar">
        <div className="profiles-sidebar-header">
          <div className="profiles-sidebar-title">
            <FolderTree size={18} className="text-primary" />
            <span>Profil Ağacı &amp; Şirketler</span>
          </div>
          <button
            type="button"
            className="btn btn-sm btn-primary"
            onClick={() => {
              setCreateParentId(null);
              setCreateType("Company");
              setCreateModalOpen(true);
            }}
          >
            <Plus size={14} /> Şirket Profili
          </button>
        </div>

        <div className="profiles-search-bar">
          <Search size={14} className="search-icon" />
          <input
            type="text"
            placeholder="Profil veya şirket ara..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
        </div>

        <div className="profiles-tree-content">
          {loading ? (
            <div className="profiles-tree-loading">
              <RefreshCw size={20} className="spinner" />
              <span>Profiller taranıyor...</span>
            </div>
          ) : tree.length === 0 ? (
            <div className="profiles-empty-state">
              <Building2 size={36} className="text-muted" />
              <p>Henüz tanımlı bir kurumsal profil bulunmuyor.</p>
              <button
                type="button"
                className="btn btn-sm btn-primary"
                onClick={() => {
                  setCreateParentId(null);
                  setCreateType("Company");
                  setCreateModalOpen(true);
                }}
              >
                İlk Şirketi Oluştur
              </button>
            </div>
          ) : (
            <div className="profile-tree-list">{tree.map((node) => renderTreeNode(node, 0))}</div>
          )}
        </div>
      </div>

      {/* SAĞ PANEL: Seçili Profil ve Politika Editörü */}
      <div className="profiles-main-editor">
        {detailLoading ? (
          <div className="profile-editor-loading">
            <RefreshCw size={28} className="spinner text-primary" />
            <span>Profil politikaları ve hiyerarşi yükleniyor...</span>
          </div>
        ) : !profileDetail || !policyDoc ? (
          <div className="profile-editor-empty">
            <Shield size={48} className="text-subtle" />
            <h3>Profil Seçilmedi</h3>
            <p>
              Düzenlemek, miras alınan politikaları incelemek veya cihazlara anlık uygulamak için sol
              ağaçtan bir profil seçin.
            </p>
          </div>
        ) : (
          <div className="profile-editor-content">
            {/* Üst Bar: Profil Başlığı, Sürüm ve Eylemler */}
            <div className="profile-editor-header">
              <div className="profile-editor-title-block">
                <div className="profile-breadcrumb">
                  {profileDetail.parentProfileId ? (
                    <span className="breadcrumb-parent">
                      Miras Alınan Üst Profil:{" "}
                      <strong>{policyDoc.profileName || "Üst Politika"}</strong>
                    </span>
                  ) : (
                    <span className="breadcrumb-root">En Üst Seviye Kök Profil (Şirket)</span>
                  )}
                </div>
                <div className="profile-title-row">
                  <h2>{profileDetail.name}</h2>
                  <span className={`badge-pill badge-${profileDetail.type.toLowerCase()}`}>
                    {profileDetail.type}
                  </span>
                  <span className="badge-pill badge-version">v{profileDetail.policyVersion}</span>
                  <span className="badge-pill badge-devices">
                    {profileDetail.deviceCount} Cihaz Bağlı
                  </span>
                </div>
              </div>

              <div className="profile-editor-actions">
                <button
                  type="button"
                  className="btn btn-outline"
                  title="Bu profili ve politikalarını kopyala"
                  onClick={() => {
                    setCloneNewName(`${profileDetail.name} (Kopya)`);
                    setCloneModalOpen(true);
                  }}
                >
                  <Copy size={15} /> Kopyala
                </button>
                <button
                  type="button"
                  className="btn btn-warning"
                  title="Bağlı tüm ajanlara anlık SignalR zorlama sinyali gönder"
                  onClick={handleApplyNow}
                  disabled={applying}
                >
                  <Zap size={15} />
                  {applying ? "Uygulanıyor..." : "Politikayı Şimdi Uygula"}
                </button>
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={handleSave}
                  disabled={saving}
                >
                  <Save size={15} />
                  {saving ? "Kaydediliyor..." : "Değişiklikleri Kaydet"}
                </button>
                <button
                  type="button"
                  className="btn btn-danger-icon"
                  title="Profili Sil"
                  onClick={() => setDeleteConfirmOpen(true)}
                >
                  <Trash2 size={16} />
                </button>
              </div>
            </div>

            {/* Politika Modül Sekmeleri */}
            <div className="profile-tabs-nav">
              <button
                type="button"
                className={`tab-item ${activeTab === "branding" ? "active" : ""}`}
                onClick={() => setActiveTab("branding")}
              >
                <Smartphone size={16} /> Kurumsal Kimlik &amp; Görsel
              </button>
              <button
                type="button"
                className={`tab-item ${activeTab === "protection" ? "active" : ""}`}
                onClick={() => setActiveTab("protection")}
              >
                <Lock size={16} /> Agent Koruma Şifresi
              </button>
              <button
                type="button"
                className={`tab-item ${activeTab === "remote_access" ? "active" : ""}`}
                onClick={() => setActiveTab("remote_access")}
              >
                <Radio size={16} /> Uzaktan Bağlantı Politikası
              </button>
              <button
                type="button"
                className={`tab-item ${activeTab === "usb" ? "active" : ""}`}
                onClick={() => setActiveTab("usb")}
              >
                <Usb size={16} /> USB Politikası
              </button>
              <button
                type="button"
                className={`tab-item ${activeTab === "effective" ? "active" : ""}`}
                onClick={() => setActiveTab("effective")}
              >
                <Info size={16} /> Etkin Politika Özeti
              </button>
              <button
                type="button"
                className={`tab-item ${activeTab === "devices" ? "active" : ""}`}
                onClick={() => setActiveTab("devices")}
              >
                <HardDrive size={16} /> Bağlı Cihazlar ({assignedDevices.length})
              </button>
            </div>

            {/* SEKME 1: Kurumsal Kimlik (Branding) */}
            {activeTab === "branding" && (
              <div className="policy-tab-pane">
                <div className="section-card">
                  <div className="section-card-header">
                    <h4>Agent Görsel Kimliği ve Markalama</h4>
                    <p>
                      Bu profile bağlı Windows istemcilerindeki NexMote bildirim alanı (tray), pencere
                      başlıkları ve destek kartında gösterilecek kimlik ayarları.
                    </p>
                  </div>

                  <div className="form-grid-2">
                    <div className="form-group">
                      <label>Profil Adı</label>
                      <input
                        type="text"
                        className="form-control"
                        value={name}
                        onChange={(e) => setName(e.target.value)}
                        placeholder="Örn: Talay Logistics - Bilgi Teknolojileri"
                      />
                    </div>

                    <div className="form-group">
                      <label>Profil Tipi</label>
                      <select
                        className="form-control"
                        value={profileType}
                        onChange={(e) => setProfileType(e.target.value)}
                      >
                        <option value="Company">Şirket (Kök Seviye)</option>
                        <option value="Department">Departman</option>
                        <option value="Location">Lokasyon / Şube</option>
                        <option value="Custom">Özel Politika Grubu</option>
                      </select>
                    </div>

                    <div className="form-group">
                      <label>Şirket Adı (Company Name)</label>
                      <input
                        type="text"
                        className="form-control"
                        value={policyDoc.branding?.companyName || ""}
                        onChange={(e) =>
                          setPolicyDoc({
                            ...policyDoc,
                            branding: { ...policyDoc.branding, companyName: e.target.value || null },
                          })
                        }
                        placeholder={
                          profileDetail.parentProfileId
                            ? `Miras: ${profileDetail.effectivePolicy.branding?.companyName || "Tanımlanmamış"}`
                            : "Örn: Talay Logistics"
                        }
                      />
                    </div>

                    <div className="form-group">
                      <label>Ajan Görünen Adı (Agent Display Name)</label>
                      <input
                        type="text"
                        className="form-control"
                        value={policyDoc.branding?.agentDisplayName || ""}
                        onChange={(e) =>
                          setPolicyDoc({
                            ...policyDoc,
                            branding: { ...policyDoc.branding, agentDisplayName: e.target.value || null },
                          })
                        }
                        placeholder={
                          profileDetail.parentProfileId
                            ? `Miras: ${profileDetail.effectivePolicy.branding?.agentDisplayName || "NexMote Agent"}`
                            : "Örn: Talay Kurumsal Destek"
                        }
                      />
                    </div>

                    <div className="form-group">
                      <label>Destek İletişim Bilgisi</label>
                      <input
                        type="text"
                        className="form-control"
                        value={policyDoc.branding?.supportContact || ""}
                        onChange={(e) =>
                          setPolicyDoc({
                            ...policyDoc,
                            branding: { ...policyDoc.branding, supportContact: e.target.value || null },
                          })
                        }
                        placeholder="Örn: Dahili: 1044 / destek@talay.com"
                      />
                    </div>

                    <div className="form-group">
                      <label>Agent Hakkında Metni</label>
                      <input
                        type="text"
                        className="form-control"
                        value={policyDoc.branding?.aboutText || ""}
                        onChange={(e) =>
                          setPolicyDoc({
                            ...policyDoc,
                            branding: { ...policyDoc.branding, aboutText: e.target.value || null },
                          })
                        }
                        placeholder="Örn: Bu cihaz Talay BT Güvenlik Politikalarına tabidir."
                      />
                    </div>
                  </div>
                </div>
              </div>
            )}

            {/* SEKME 2: Agent Koruma Şifresi */}
            {activeTab === "protection" && (
              <div className="policy-tab-pane">
                <div className="section-card">
                  <div className="section-card-header">
                    <h4>Agent Kapatma &amp; Servis Güvenlik Koruması</h4>
                    <p>
                      Kullanıcıların ajanı kapatmasını, kaldırmasını veya Windows arka plan servisini
                      durdurmasını koruma şifresine bağlayın. Parola PBKDF2 ile güvenle hashlenir.
                    </p>
                  </div>

                  <div className="protection-toggles">
                    <div className="toggle-row">
                      <div className="toggle-info">
                        <span className="toggle-title">Agent Koruma Kilidi Aktif</span>
                        <span className="toggle-desc">
                          Aktif olduğunda ajan kapatılırken veya ayarlara girilirken koruma parolası
                          zorunlu tutulur.
                        </span>
                      </div>
                      <label className="switch">
                        <input
                          type="checkbox"
                          checked={policyDoc.protection?.agentProtection ?? false}
                          onChange={(e) =>
                            setPolicyDoc({
                              ...policyDoc,
                              protection: {
                                ...policyDoc.protection,
                                agentProtection: e.target.checked,
                              },
                            })
                          }
                        />
                        <span className="slider round"></span>
                      </label>
                    </div>

                    <div className="form-group" style={{ maxWidth: 450, marginTop: 16 }}>
                      <label>
                        Yeni Koruma Şifresi Belirle{" "}
                        <span className="text-muted">(Değiştirmek istemiyorsanız boş bırakın)</span>
                      </label>
                      <div className="password-input-group">
                        <input
                          type={showPassword ? "text" : "password"}
                          className="form-control"
                          value={newPassword}
                          onChange={(e) => setNewPassword(e.target.value)}
                          placeholder="Güçlü bir koruma parolası girin..."
                        />
                        <button
                          type="button"
                          className="btn btn-outline btn-sm"
                          onClick={() => setShowPassword(!showPassword)}
                        >
                          {showPassword ? "Gizle" : "Göster"}
                        </button>
                      </div>
                    </div>

                    <hr className="divider" />

                    <div className="toggle-row">
                      <div className="toggle-info">
                        <span className="toggle-title">Ajanın Kapatılmasına İzin Ver (Allow Exit)</span>
                        <span className="toggle-desc">
                          Kapalıysa şifre girilmeden sağ alttaki menüden ajandan çıkış yapılamaz.
                        </span>
                      </div>
                      <label className="switch">
                        <input
                          type="checkbox"
                          checked={policyDoc.protection?.allowAgentExit ?? true}
                          onChange={(e) =>
                            setPolicyDoc({
                              ...policyDoc,
                              protection: {
                                ...policyDoc.protection,
                                allowAgentExit: e.target.checked,
                              },
                            })
                          }
                        />
                        <span className="slider round"></span>
                      </label>
                    </div>

                    <div className="toggle-row">
                      <div className="toggle-info">
                        <span className="toggle-title">
                          Windows Servisinin Durdurulmasına İzin Ver (Allow Service Stop)
                        </span>
                        <span className="toggle-desc">
                          Kapalıysa LocalSystem servisi durdurma isteklerini reddeder ve Watchdog anında
                          yeniden başlatır.
                        </span>
                      </div>
                      <label className="switch">
                        <input
                          type="checkbox"
                          checked={policyDoc.protection?.allowServiceStop ?? false}
                          onChange={(e) =>
                            setPolicyDoc({
                              ...policyDoc,
                              protection: {
                                ...policyDoc.protection,
                                allowServiceStop: e.target.checked,
                              },
                            })
                          }
                        />
                        <span className="slider round"></span>
                      </label>
                    </div>

                    <div className="toggle-row">
                      <div className="toggle-info">
                        <span className="toggle-title">
                          Ajanın Kaldırılmasına İzin Ver (Allow Uninstall)
                        </span>
                        <span className="toggle-desc">
                          Kapalıysa Denetim Masası veya msiexec üzerinden parola olmadan kaldırma engellenir.
                        </span>
                      </div>
                      <label className="switch">
                        <input
                          type="checkbox"
                          checked={policyDoc.protection?.allowAgentUninstall ?? false}
                          onChange={(e) =>
                            setPolicyDoc({
                              ...policyDoc,
                              protection: {
                                ...policyDoc.protection,
                                allowAgentUninstall: e.target.checked,
                              },
                            })
                          }
                        />
                        <span className="slider round"></span>
                      </label>
                    </div>
                  </div>
                </div>
              </div>
            )}

            {/* SEKME 3: Uzaktan Bağlantı Politikası */}
            {activeTab === "remote_access" && (
              <div className="policy-tab-pane">
                <div className="section-card">
                  <div className="section-card-header">
                    <h4>Uzaktan Bağlantı Modu &amp; Kullanıcı Onay Davranışı</h4>
                    <p>
                      Teknisyen bu profile bağlı bir cihaza bağlandığında uygulanacak yetki ve onay
                      kuralları.
                    </p>
                  </div>

                  <div className="radio-modes-container">
                    <label
                      className={`mode-card ${
                        (policyDoc.remoteAccess?.mode || "unattended") === "unattended"
                          ? "selected"
                          : ""
                      }`}
                    >
                      <input
                        type="radio"
                        name="remoteAccessMode"
                        value="unattended"
                        checked={(policyDoc.remoteAccess?.mode || "unattended") === "unattended"}
                        onChange={() =>
                          setPolicyDoc({
                            ...policyDoc,
                            remoteAccess: { ...policyDoc.remoteAccess, mode: "unattended" },
                          })
                        }
                      />
                      <div className="mode-content">
                        <div className="mode-title">
                          <Zap size={18} className="text-primary" /> Mod 1 — İzin İstemeden Bağlan
                          (Unattended)
                        </div>
                        <div className="mode-desc">
                          Yetkili destek personeli doğrudan canlı masaüstüne bağlanır. Sunucular, kiosklar
                          ve BT makineleri için önerilir.
                        </div>
                      </div>
                    </label>

                    <label
                      className={`mode-card ${
                        policyDoc.remoteAccess?.mode === "prompt" ? "selected" : ""
                      }`}
                    >
                      <input
                        type="radio"
                        name="remoteAccessMode"
                        value="prompt"
                        checked={policyDoc.remoteAccess?.mode === "prompt"}
                        onChange={() =>
                          setPolicyDoc({
                            ...policyDoc,
                            remoteAccess: { ...policyDoc.remoteAccess, mode: "prompt" },
                          })
                        }
                      />
                      <div className="mode-content">
                        <div className="mode-title">
                          <Shield size={18} className="text-emerald" /> Mod 2 — Kullanıcıdan İzin İste
                          (Prompt Consent)
                        </div>
                        <div className="mode-desc">
                          Teknisyen bağlandığında ekranda "IT Destek birimi bilgisayarınıza bağlanmak istiyor"
                          diyaloğu açılır. Kullanıcı onay verirse bağlanılır.
                        </div>
                      </div>
                    </label>

                    <label
                      className={`mode-card ${
                        policyDoc.remoteAccess?.mode === "auto_accept_idle" ? "selected" : ""
                      }`}
                    >
                      <input
                        type="radio"
                        name="remoteAccessMode"
                        value="auto_accept_idle"
                        checked={policyDoc.remoteAccess?.mode === "auto_accept_idle"}
                        onChange={() =>
                          setPolicyDoc({
                            ...policyDoc,
                            remoteAccess: { ...policyDoc.remoteAccess, mode: "auto_accept_idle" },
                          })
                        }
                      />
                      <div className="mode-content">
                        <div className="mode-title">
                          <RefreshCw size={18} className="text-amber" /> Mod 3 — Kullanıcı Yoksa Otomatik
                          Bağlan (Idle Fallback)
                        </div>
                        <div className="mode-desc">
                          Kullanıcı bilgisayar başındaysa izin istenir; eğer belirtilen süre boyunca klavye/fare
                          hareketi yoksa bağlantı otomatik kabul edilir.
                        </div>
                      </div>
                    </label>
                  </div>

                  {/* Mod 2 & 3 Ek Ayarları */}
                  {policyDoc.remoteAccess?.mode !== "unattended" && (
                    <div className="form-grid-3" style={{ marginTop: 20 }}>
                      <div className="form-group">
                        <label>Onay Zaman Aşımı (Saniye)</label>
                        <input
                          type="number"
                          className="form-control"
                          min="5"
                          max="300"
                          value={policyDoc.remoteAccess?.promptTimeoutSeconds ?? 30}
                          onChange={(e) =>
                            setPolicyDoc({
                              ...policyDoc,
                              remoteAccess: {
                                ...policyDoc.remoteAccess,
                                promptTimeoutSeconds: parseInt(e.target.value) || 30,
                              },
                            })
                          }
                        />
                      </div>

                      <div className="form-group">
                        <label>Zaman Aşımında Varsayılan Eylem</label>
                        <select
                          className="form-control"
                          value={policyDoc.remoteAccess?.defaultAction || "deny"}
                          onChange={(e) =>
                            setPolicyDoc({
                              ...policyDoc,
                              remoteAccess: {
                                ...policyDoc.remoteAccess,
                                defaultAction: e.target.value,
                              },
                            })
                          }
                        >
                          <option value="deny">Otomatik Reddet</option>
                          <option value="allow">Otomatik Kabul Et</option>
                        </select>
                      </div>

                      {policyDoc.remoteAccess?.mode === "auto_accept_idle" && (
                        <div className="form-group">
                          <label>Boşta Kalma Süresi (Dakika)</label>
                          <select
                            className="form-control"
                            value={policyDoc.remoteAccess?.idleTimeoutMinutes ?? 5}
                            onChange={(e) =>
                              setPolicyDoc({
                                ...policyDoc,
                                remoteAccess: {
                                  ...policyDoc.remoteAccess,
                                  idleTimeoutMinutes: parseInt(e.target.value) || 5,
                                },
                              })
                            }
                          >
                            <option value={5}>5 Dakika</option>
                            <option value={10}>10 Dakika</option>
                            <option value={15}>15 Dakika</option>
                            <option value={30}>30 Dakika</option>
                          </select>
                        </div>
                      )}
                    </div>
                  )}

                  <hr className="divider" />

                  {/* Oturum Özellik İzinleri */}
                  <div className="protection-toggles">
                    <div className="toggle-row">
                      <div className="toggle-info">
                        <span className="toggle-title">Canlı Oturum Uyarı Bandı Göster</span>
                        <span className="toggle-desc">
                          Teknisyen bağlandığında hedef ekranın üstünde bağlantı bandı gösterilir.
                        </span>
                      </div>
                      <label className="switch">
                        <input
                          type="checkbox"
                          checked={policyDoc.remoteAccess?.showConnectionBanner ?? true}
                          onChange={(e) =>
                            setPolicyDoc({
                              ...policyDoc,
                              remoteAccess: {
                                ...policyDoc.remoteAccess,
                                showConnectionBanner: e.target.checked,
                              },
                            })
                          }
                        />
                        <span className="slider round"></span>
                      </label>
                    </div>

                    <div className="toggle-row">
                      <div className="toggle-info">
                        <span className="toggle-title">Sadece İzleme Modu (View Only)</span>
                        <span className="toggle-desc">
                          Açık olduğunda teknisyen ekranı görür ancak klavye ve fare girdisi gönderemez.
                        </span>
                      </div>
                      <label className="switch">
                        <input
                          type="checkbox"
                          checked={policyDoc.remoteAccess?.viewOnlyMode ?? false}
                          onChange={(e) =>
                            setPolicyDoc({
                              ...policyDoc,
                              remoteAccess: {
                                ...policyDoc.remoteAccess,
                                viewOnlyMode: e.target.checked,
                              },
                            })
                          }
                        />
                        <span className="slider round"></span>
                      </label>
                    </div>

                    <div className="toggle-row">
                      <div className="toggle-info">
                        <span className="toggle-title">Uzak Terminal (CMD / PowerShell) İzni</span>
                        <span className="toggle-desc">
                          Teknisyenin konsoldan uzak komut satırı çalıştırmasına izin verilir.
                        </span>
                      </div>
                      <label className="switch">
                        <input
                          type="checkbox"
                          checked={policyDoc.remoteAccess?.allowRemoteTerminal ?? true}
                          onChange={(e) =>
                            setPolicyDoc({
                              ...policyDoc,
                              remoteAccess: {
                                ...policyDoc.remoteAccess,
                                allowRemoteTerminal: e.target.checked,
                              },
                            })
                          }
                        />
                        <span className="slider round"></span>
                      </label>
                    </div>

                    <div className="toggle-row">
                      <div className="toggle-info">
                        <span className="toggle-title">Pano (Clipboard) Paylaşımı İzni</span>
                        <span className="toggle-desc">
                          Cihaz ile teknisyen arasında metin kopyalama/yapıştırma eşitlemesi.
                        </span>
                      </div>
                      <label className="switch">
                        <input
                          type="checkbox"
                          checked={policyDoc.remoteAccess?.allowClipboard ?? true}
                          onChange={(e) =>
                            setPolicyDoc({
                              ...policyDoc,
                              remoteAccess: {
                                ...policyDoc.remoteAccess,
                                allowClipboard: e.target.checked,
                              },
                            })
                          }
                        />
                        <span className="slider round"></span>
                      </label>
                    </div>
                  </div>
                </div>
              </div>
            )}

            {/* SEKME 4: USB Politikası */}
            {activeTab === "usb" && (
              <div className="policy-tab-pane">
                <div className="section-card">
                  <div className="section-card-header">
                    <h4>USB Giriş ve Harici Depolama Kontrol Politikası</h4>
                    <p>
                      Windows çekirdek ve kayıt defteri seviyesinde USB denetimi (yeniden başlatma
                      gerektirmeden anında uygulanır).
                    </p>
                  </div>

                  <div className="radio-modes-container">
                    <label
                      className={`mode-card ${
                        (policyDoc.usb?.mode || "allow_all") === "allow_all" ? "selected" : ""
                      }`}
                    >
                      <input
                        type="radio"
                        name="usbMode"
                        value="allow_all"
                        checked={(policyDoc.usb?.mode || "allow_all") === "allow_all"}
                        onChange={() =>
                          setPolicyDoc({
                            ...policyDoc,
                            usb: { ...policyDoc.usb, mode: "allow_all" },
                          })
                        }
                      />
                      <div className="mode-content">
                        <div className="mode-title">
                          <Usb size={18} className="text-emerald" /> USB Tamamen Serbest
                        </div>
                        <div className="mode-desc">
                          Tüm USB cihazları (klavye, fare, flash bellek, harici disk) kısıtlamasız çalışır.
                        </div>
                      </div>
                    </label>

                    <label
                      className={`mode-card ${
                        policyDoc.usb?.mode === "block_storage" ? "selected" : ""
                      }`}
                    >
                      <input
                        type="radio"
                        name="usbMode"
                        value="block_storage"
                        checked={policyDoc.usb?.mode === "block_storage"}
                        onChange={() =>
                          setPolicyDoc({
                            ...policyDoc,
                            usb: { ...policyDoc.usb, mode: "block_storage" },
                          })
                        }
                      />
                      <div className="mode-content">
                        <div className="mode-title">
                          <HardDrive size={18} className="text-amber" /> Sadece USB Depolama Yasak
                          (USBSTOR Block)
                        </div>
                        <div className="mode-desc">
                          Klavye, fare ve web kameraları çalışır; flash bellek ve taşınabilir diskler
                          engellenir.
                        </div>
                      </div>
                    </label>

                    <label
                      className={`mode-card ${policyDoc.usb?.mode === "read_only" ? "selected" : ""}`}
                    >
                      <input
                        type="radio"
                        name="usbMode"
                        value="read_only"
                        checked={policyDoc.usb?.mode === "read_only"}
                        onChange={() =>
                          setPolicyDoc({
                            ...policyDoc,
                            usb: { ...policyDoc.usb, mode: "read_only" },
                          })
                        }
                      />
                      <div className="mode-content">
                        <div className="mode-title">
                          <Lock size={18} className="text-primary" /> USB Salt Okunur (Read Only -
                          WriteProtect)
                        </div>
                        <div className="mode-desc">
                          USB belleklerden dosya okunabilir ancak bilgisayardan USB belleğe veri yazılamaz/kopyalanamaz.
                        </div>
                      </div>
                    </label>

                    <label
                      className={`mode-card ${policyDoc.usb?.mode === "whitelist" ? "selected" : ""}`}
                    >
                      <input
                        type="radio"
                        name="usbMode"
                        value="whitelist"
                        checked={policyDoc.usb?.mode === "whitelist"}
                        onChange={() =>
                          setPolicyDoc({
                            ...policyDoc,
                            usb: { ...policyDoc.usb, mode: "whitelist" },
                          })
                        }
                      />
                      <div className="mode-content">
                        <div className="mode-title">
                          <ShieldCheck size={18} className="text-purple" /> Sadece İzin Verilen (Whitelist)
                          USB Cihazları
                        </div>
                        <div className="mode-desc">
                          Yalnızca VID/PID veya seri numarası listede kayıtlı güvenli kurumsal USB cihazlarına izin verilir.
                        </div>
                      </div>
                    </label>

                    <label
                      className={`mode-card ${policyDoc.usb?.mode === "block_all" ? "selected" : ""}`}
                    >
                      <input
                        type="radio"
                        name="usbMode"
                        value="block_all"
                        checked={policyDoc.usb?.mode === "block_all"}
                        onChange={() =>
                          setPolicyDoc({
                            ...policyDoc,
                            usb: { ...policyDoc.usb, mode: "block_all" },
                          })
                        }
                      />
                      <div className="mode-content">
                        <div className="mode-title">
                          <ShieldAlert size={18} className="text-danger" /> USB Tamamen Yasak (Tam
                          Engelleme)
                        </div>
                        <div className="mode-desc">
                          Makinadaki tüm USB denetleyicileri kilitlenir. Yüksek güvenlikli sunucular için
                          kullanılır.
                        </div>
                      </div>
                    </label>
                  </div>

                  {/* Whitelist Cihaz Listesi Tablosu */}
                  {policyDoc.usb?.mode === "whitelist" && (
                    <div className="whitelist-section" style={{ marginTop: 24 }}>
                      <div className="whitelist-header">
                        <h5>İzin Verilen (Beyaz Liste) USB Cihazları</h5>
                        <button
                          type="button"
                          className="btn btn-sm btn-outline"
                          onClick={() => setUsbModalOpen(true)}
                        >
                          <Plus size={14} /> Cihaz Ekle
                        </button>
                      </div>

                      {(!policyDoc.usb?.whitelist || policyDoc.usb.whitelist.length === 0) ? (
                        <div className="empty-sub-list">
                          Henüz beyaz listeye eklenmiş bir USB cihazı yok.
                        </div>
                      ) : (
                        <table className="table-custom">
                          <thead>
                            <tr>
                              <th>Cihaz Tanımı</th>
                              <th>Vendor ID (VID)</th>
                              <th>Product ID (PID)</th>
                              <th>Seri Numarası</th>
                              <th style={{ width: 60 }}>İşlem</th>
                            </tr>
                          </thead>
                          <tbody>
                            {policyDoc.usb.whitelist.map((item) => (
                              <tr key={item.id}>
                                <td>
                                  <strong>{item.name}</strong>
                                </td>
                                <td>{item.vendorId || "—"}</td>
                                <td>{item.productId || "—"}</td>
                                <td>{item.serialNumber || "—"}</td>
                                <td>
                                  <button
                                    type="button"
                                    className="btn-icon-xs text-danger"
                                    onClick={() => removeUsbWhitelistItem(item.id)}
                                    title="Listeden Kaldır"
                                  >
                                    <Trash2 size={14} />
                                  </button>
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      )}
                    </div>
                  )}
                </div>
              </div>
            )}

            {/* SEKME 5: Etkin Politika Özeti (Inheritance Tree Preview) */}
            {activeTab === "effective" && (
              <div className="policy-tab-pane">
                <div className="section-card">
                  <div className="section-card-header">
                    <h4>Miras ve Override Edilmiş Etkin Politika Matrisi</h4>
                    <p>
                      Bu profil altındaki bir cihaza en tepedeki şirketten bu seviyeye kadar hangi
                      kuralların miras alındığını, hangilerinin bu seviyede ezildiğini (override)
                      gösterir.
                    </p>
                  </div>

                  <table className="table-custom">
                    <thead>
                      <tr>
                        <th>Ayar / Politika Alanı</th>
                        <th>Bu Profil Seviyesi (Override)</th>
                        <th>Nihai Etkin Değer (Effective)</th>
                        <th>Durum</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr>
                        <td>Şirket Adı</td>
                        <td>{policyDoc.branding?.companyName || "—"}</td>
                        <td>
                          <strong>
                            {profileDetail.effectivePolicy.branding?.companyName || "—"}
                          </strong>
                        </td>
                        <td>
                          {policyDoc.branding?.companyName ? (
                            <span className="badge badge-warning">Özelleştirildi (Override)</span>
                          ) : (
                            <span className="badge badge-subtle">Miras Alındı</span>
                          )}
                        </td>
                      </tr>
                      <tr>
                        <td>Ajan Görünen Adı</td>
                        <td>{policyDoc.branding?.agentDisplayName || "—"}</td>
                        <td>
                          <strong>
                            {profileDetail.effectivePolicy.branding?.agentDisplayName || "NexMote Agent"}
                          </strong>
                        </td>
                        <td>
                          {policyDoc.branding?.agentDisplayName ? (
                            <span className="badge badge-warning">Özelleştirildi (Override)</span>
                          ) : (
                            <span className="badge badge-subtle">Miras Alındı</span>
                          )}
                        </td>
                      </tr>
                      <tr>
                        <td>Agent Koruma Kilidi</td>
                        <td>{policyDoc.protection?.agentProtection != null ? (policyDoc.protection.agentProtection ? "Aktif" : "Pasif") : "—"}</td>
                        <td>
                          <strong>
                            {profileDetail.effectivePolicy.protection?.agentProtection ? "Aktif" : "Pasif"}
                          </strong>
                        </td>
                        <td>
                          {policyDoc.protection?.agentProtection != null ? (
                            <span className="badge badge-warning">Özelleştirildi (Override)</span>
                          ) : (
                            <span className="badge badge-subtle">Miras Alındı</span>
                          )}
                        </td>
                      </tr>
                      <tr>
                        <td>Uzaktan Bağlantı Modu</td>
                        <td>{policyDoc.remoteAccess?.mode || "—"}</td>
                        <td>
                          <strong>
                            {profileDetail.effectivePolicy.remoteAccess?.mode || "unattended"}
                          </strong>
                        </td>
                        <td>
                          {policyDoc.remoteAccess?.mode ? (
                            <span className="badge badge-warning">Özelleştirildi (Override)</span>
                          ) : (
                            <span className="badge badge-subtle">Miras Alındı</span>
                          )}
                        </td>
                      </tr>
                      <tr>
                        <td>USB Kontrol Modu</td>
                        <td>{policyDoc.usb?.mode || "—"}</td>
                        <td>
                          <strong>{profileDetail.effectivePolicy.usb?.mode || "allow_all"}</strong>
                        </td>
                        <td>
                          {policyDoc.usb?.mode ? (
                            <span className="badge badge-warning">Özelleştirildi (Override)</span>
                          ) : (
                            <span className="badge badge-subtle">Miras Alındı</span>
                          )}
                        </td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </div>
            )}

            {/* SEKME 6: Bağlı Cihazlar */}
            {activeTab === "devices" && (
              <div className="policy-tab-pane">
                <div className="section-card">
                  <div className="section-card-header">
                    <h4>Bu Profile Atanmış Cihazlar ({assignedDevices.length})</h4>
                    <p>
                      Bu profildeki politikaları doğrudan uygulayan veya cihaz seviyesinde özel override
                      uygulanmış makineler.
                    </p>
                  </div>

                  {assignedDevices.length === 0 ? (
                    <div className="empty-sub-list">
                      Bu profile henüz atanmış bir istemci cihaz bulunmuyor. Cihazlar listesinden toplu
                      veya tekil olarak bu profili atayabilirsiniz.
                    </div>
                  ) : (
                    <table className="table-custom">
                      <thead>
                        <tr>
                          <th>Cihaz Adı</th>
                          <th>Durum</th>
                          <th>IP Adresi</th>
                          <th>Aktif Kullanıcı</th>
                          <th>Politika Sürümü</th>
                          <th>Özel Override</th>
                          <th>İşlem</th>
                        </tr>
                      </thead>
                      <tbody>
                        {assignedDevices.map((dev) => {
                          const isOutdated = (dev.appliedPolicyVersion ?? 0) < profileDetail.policyVersion;
                          return (
                            <tr key={dev.id}>
                              <td>
                                <strong>{dev.deviceName}</strong>
                                <span className="text-dim" style={{ display: "block", fontSize: "11px" }}>
                                  {dev.domainName}
                                </span>
                              </td>
                              <td>
                                <span className={`status-badge ${dev.isOnline ? "online" : "offline"}`}>
                                  {dev.isOnline ? "Çevrimiçi" : "Çevrimdışı"}
                                </span>
                              </td>
                              <td>{dev.ipAddress || "—"}</td>
                              <td>{dev.activeUser || "—"}</td>
                              <td>
                                <span>v{dev.appliedPolicyVersion ?? 0}</span>
                                {isOutdated && (
                                  <span
                                    className="badge badge-danger"
                                    style={{ marginLeft: 6 }}
                                    title="Cihazın uyguladığı politika sürümü sunucudakinden geride"
                                  >
                                    Policy Outdated
                                  </span>
                                )}
                              </td>
                              <td>
                                {dev.hasCustomOverride ? (
                                  <span className="badge badge-warning">Özel Politika</span>
                                ) : (
                                  <span className="badge badge-subtle">Profil Mirası</span>
                                )}
                              </td>
                              <td>
                                {onSelectDevice && (
                                  <button
                                    type="button"
                                    className="btn btn-xs btn-outline"
                                    onClick={() => onSelectDevice(dev.id)}
                                  >
                                    İncele
                                  </button>
                                )}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  )}
                </div>
              </div>
            )}
          </div>
        )}
      </div>

      {/* MODAL: Yeni Profil Oluştur */}
      {createModalOpen && (
        <div className="modal-backdrop">
          <div className="modal-dialog">
            <form onSubmit={handleCreateSubmit}>
              <div className="modal-header">
                <h5>
                  {createParentId ? "Yeni Alt Profil Oluştur" : "Yeni Şirket Profili (Kök)"}
                </h5>
                <button
                  type="button"
                  className="modal-close-btn"
                  onClick={() => setCreateModalOpen(false)}
                >
                  ×
                </button>
              </div>
              <div className="modal-body">
                <div className="form-group">
                  <label>Profil Adı</label>
                  <input
                    type="text"
                    className="form-control"
                    required
                    placeholder="Örn: Talay Logistics veya Bilgi Teknolojileri"
                    value={createName}
                    onChange={(e) => setCreateName(e.target.value)}
                  />
                </div>
                <div className="form-group" style={{ marginTop: 14 }}>
                  <label>Profil Tipi</label>
                  <select
                    className="form-control"
                    value={createType}
                    onChange={(e) => setCreateType(e.target.value)}
                  >
                    <option value="Company">Şirket (Company)</option>
                    <option value="Department">Departman (Department)</option>
                    <option value="Location">Lokasyon / Şube (Location)</option>
                    <option value="Custom">Özel Grup (Custom)</option>
                  </select>
                </div>
              </div>
              <div className="modal-footer">
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => setCreateModalOpen(false)}
                >
                  İptal
                </button>
                <button type="submit" className="btn btn-primary">
                  Oluştur
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* MODAL: Profil Kopyala / Klonla */}
      {cloneModalOpen && (
        <div className="modal-backdrop">
          <div className="modal-dialog">
            <form onSubmit={handleCloneSubmit}>
              <div className="modal-header">
                <h5>Profili Kopyala</h5>
                <button
                  type="button"
                  className="modal-close-btn"
                  onClick={() => setCloneModalOpen(false)}
                >
                  ×
                </button>
              </div>
              <div className="modal-body">
                <p className="text-muted" style={{ marginBottom: 14 }}>
                  "{profileDetail?.name}" profili tüm politika ayarları ve alt hiyerarşisiyle birlikte
                  yeni bir profil olarak kopyalanacaktır.
                </p>
                <div className="form-group">
                  <label>Yeni Profil Adı</label>
                  <input
                    type="text"
                    className="form-control"
                    required
                    value={cloneNewName}
                    onChange={(e) => setCloneNewName(e.target.value)}
                  />
                </div>
              </div>
              <div className="modal-footer">
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => setCloneModalOpen(false)}
                >
                  İptal
                </button>
                <button type="submit" className="btn btn-primary">
                  Kopyasını Oluştur
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* MODAL: Profil Silme Onayı */}
      {deleteConfirmOpen && (
        <div className="modal-backdrop">
          <div className="modal-dialog">
            <div className="modal-header">
              <h5 className="text-danger">Profili Sil</h5>
              <button
                type="button"
                className="modal-close-btn"
                onClick={() => setDeleteConfirmOpen(false)}
              >
                ×
              </button>
            </div>
            <div className="modal-body">
              <p>
                <strong>"{profileDetail?.name}"</strong> profilini silmek istediğinize emin misiniz?
              </p>
              <p className="text-muted" style={{ fontSize: "12.5px" }}>
                Bağlı alt profiller veya atanmış cihazlar varsa sistem silme işlemini engelleyecektir.
              </p>
            </div>
            <div className="modal-footer">
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => setDeleteConfirmOpen(false)}
              >
                İptal
              </button>
              <button type="button" className="btn btn-danger" onClick={handleDeleteSubmit}>
                Profili Kalıcı Olarak Sil
              </button>
            </div>
          </div>
        </div>
      )}

      {/* MODAL: USB Whitelist Cihaz Ekle */}
      {usbModalOpen && (
        <div className="modal-backdrop">
          <div className="modal-dialog">
            <div className="modal-header">
              <h5>İzin Verilecek USB Cihazı Ekle</h5>
              <button
                type="button"
                className="modal-close-btn"
                onClick={() => setUsbModalOpen(false)}
              >
                ×
              </button>
            </div>
            <div className="modal-body">
              <div className="form-group">
                <label>Cihaz Adı / Tanımı *</label>
                <input
                  type="text"
                  className="form-control"
                  required
                  placeholder="Örn: IT Kingston DataTraveler 64GB"
                  value={usbName}
                  onChange={(e) => setUsbName(e.target.value)}
                />
              </div>
              <div className="form-grid-2" style={{ marginTop: 12 }}>
                <div className="form-group">
                  <label>Vendor ID (VID)</label>
                  <input
                    type="text"
                    className="form-control"
                    placeholder="Örn: 0951"
                    value={usbVendorId}
                    onChange={(e) => setUsbVendorId(e.target.value)}
                  />
                </div>
                <div className="form-group">
                  <label>Product ID (PID)</label>
                  <input
                    type="text"
                    className="form-control"
                    placeholder="Örn: 1666"
                    value={usbProductId}
                    onChange={(e) => setUsbProductId(e.target.value)}
                  />
                </div>
              </div>
              <div className="form-group" style={{ marginTop: 12 }}>
                <label>Seri Numarası (Varsa)</label>
                <input
                  type="text"
                  className="form-control"
                  placeholder="Örn: 0014D1187C17F07037190BE7"
                  value={usbSerial}
                  onChange={(e) => setUsbSerial(e.target.value)}
                />
              </div>
            </div>
            <div className="modal-footer">
              <button
                type="button"
                className="btn btn-outline"
                onClick={() => setUsbModalOpen(false)}
              >
                İptal
              </button>
              <button
                type="button"
                className="btn btn-primary"
                onClick={addUsbWhitelistItem}
                disabled={!usbName.trim()}
              >
                Beyaz Listeye Ekle
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
