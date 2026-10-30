-- Argo CD health for kagent.dev/Agent (argocd-cm key resource.customizations.health.kagent.dev_Agent). Without it Argo
-- reports no health for an Agent and Skanyxx's deploy status stays "syncing" (D363).
-- Ready=True → Healthy; scaled to zero (the owner's stop or a direct suspend) → Suspended; Accepted=False → Degraded.
hs = { status = "Progressing", message = "Waiting for kagent" }
if obj.spec ~= nil and obj.spec.declarative ~= nil and obj.spec.declarative.deployment ~= nil
    and obj.spec.declarative.deployment.replicas == 0 then
  hs.status = "Suspended"
  hs.message = "Scaled to zero"
  return hs
end
if obj.status ~= nil and obj.status.conditions ~= nil then
  for _, c in ipairs(obj.status.conditions) do
    if c.type == "Accepted" and c.status == "False" then
      hs.status = "Degraded"
      hs.message = c.message or "Not accepted by kagent"
      return hs
    end
    if c.type == "Ready" and c.status == "True" then
      hs.status = "Healthy"
      hs.message = c.message or "Ready"
    end
  end
end
return hs
