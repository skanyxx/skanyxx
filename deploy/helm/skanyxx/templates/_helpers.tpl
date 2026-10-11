{{/* Skanyxx's own name: the release name when it already says skanyxx (helm install skanyxx …), else <release>-skanyxx. */}}
{{- define "skanyxx.fullname" -}}
{{- if contains "skanyxx" .Release.Name -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-skanyxx" .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{/* A bundled component's name: <release>-<component>. */}}
{{- define "skanyxx.component" -}}
{{- printf "%s-%s" .ctx.Release.Name .name | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "skanyxx.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: skanyxx
{{- end -}}

{{/* Selector labels of one component: (dict "ctx" $ "name" "postgres"). */}}
{{- define "skanyxx.selectorLabels" -}}
app.kubernetes.io/name: {{ .name }}
app.kubernetes.io/instance: {{ .ctx.Release.Name }}
{{- end -}}

{{/*
A generated secret value that survives upgrades: the value already in the cluster when there is one, else a fresh
random one. (dict "ctx" $ "secret" "<name>" "key" "<key>" "length" 32). `helm template` never sees the cluster, so
it always prints fresh values.
*/}}
{{- define "skanyxx.secretValue" -}}
{{- $s := lookup "v1" "Secret" .ctx.Release.Namespace .secret -}}
{{- if and $s $s.data (hasKey $s.data .key) -}}
{{- index $s.data .key | b64dec -}}
{{- else -}}
{{- randAlphaNum (int .length) -}}
{{- end -}}
{{- end -}}

{{- define "skanyxx.publicUrl" -}}
{{- if .Values.skanyxx.publicUrl -}}
{{- .Values.skanyxx.publicUrl -}}
{{- else if and .Values.ingress.enabled .Values.ingress.hosts -}}
{{- printf "https://%s" (first .Values.ingress.hosts) -}}
{{- end -}}
{{- end -}}

{{- define "skanyxx.kagentUrl" -}}
{{- if .Values.kagent.enabled -}}
{{- printf "http://kagent-controller.%s.svc:8083" .Release.Namespace -}}
{{- else -}}
{{- required "kagent.enabled=false needs skanyxx.kagent.url (the kagent controller Skanyxx calls)" .Values.skanyxx.kagent.url -}}
{{- end -}}
{{- end -}}

{{- define "skanyxx.dbSecret" -}}
{{- if and .Values.postgresql.enabled (not .Values.postgresql.existingSecret) -}}
{{- include "skanyxx.component" (dict "ctx" . "name" "skanyxx-db") -}}
{{- else -}}
{{- required "postgresql.enabled=false or postgresql.existingSecret needs skanyxx.database.existingSecret (ConnectionStrings__Identity, __Memory, __Tickets)" .Values.skanyxx.database.existingSecret -}}
{{- end -}}
{{- end -}}

{{- define "skanyxx.bootstrapSecret" -}}
{{- default (include "skanyxx.fullname" .) .Values.skanyxx.bootstrapToken.existingSecret -}}
{{- end -}}

{{/* AllowedHosts: the ingress hosts, the Service as kagent and in-cluster callers name it, localhost, extras. */}}
{{- define "skanyxx.allowedHosts" -}}
{{- $svc := include "skanyxx.fullname" . -}}
{{- $ns := .Release.Namespace -}}
{{- $hosts := concat .Values.ingress.hosts (list $svc (printf "%s.%s" $svc $ns) (printf "%s.%s.svc" $svc $ns) (printf "%s.%s.svc.cluster.local" $svc $ns) "localhost") .Values.skanyxx.extraAllowedHosts -}}
{{- with include "skanyxx.publicUrl" . -}}
{{- $hosts = append $hosts (urlParse .).hostname -}}
{{- end -}}
{{- if include "skanyxx.sandboxMemory" . -}}
{{- /* Sandboxes call the sandbox listener by its own Service name (Sandboxes:MemoryMcpUrl). */ -}}
{{- $hosts = concat $hosts (list (printf "%s-sandbox-mcp.%s.svc" $svc $ns) (printf "%s-sandbox-mcp.%s.svc.cluster.local" $svc $ns)) -}}
{{- end -}}
{{- $hosts | uniq | join ";" -}}
{{- end -}}

{{/* An image reference: repository:tag, plus @digest when one is set. (dict "image" <values.image> "tag" <default tag>) */}}
{{- define "skanyxx.image" -}}
{{- $ref := printf "%s:%s" .image.repository (default .tag .image.tag) -}}
{{- if .image.digest -}}
{{- $ref = printf "%s@%s" $ref .image.digest -}}
{{- end -}}
{{- $ref -}}
{{- end -}}

{{/* Each bundled store's credential Secret: the operator's existingSecret, else the one the chart generates. */}}
{{- define "skanyxx.postgresSecret" -}}
{{- default (include "skanyxx.component" (dict "ctx" . "name" "postgres")) .Values.postgresql.existingSecret -}}
{{- end -}}

{{- define "skanyxx.minioSecret" -}}
{{- default (include "skanyxx.component" (dict "ctx" . "name" "minio")) .Values.minio.existingSecret -}}
{{- end -}}

{{- define "skanyxx.giteaSecret" -}}
{{- default (include "skanyxx.component" (dict "ctx" . "name" "gitea")) .Values.gitea.existingSecret -}}
{{- end -}}

{{/*
Generated Secrets outlive the release (helm.sh/resource-policy: keep): the PVCs do, and a reinstall that drew new
passwords would not match the roles initdb created on the kept volume (D139). The sandbox fence policies use it too:
sandboxes may outlive the release.
*/}}
{{- define "skanyxx.keepSecret" -}}
annotations:
  helm.sh/resource-policy: keep
{{- end -}}

{{/*
An image of the installed AX: repository[:tag][@digest]. (dict "image" … "what" "<values path>"). ax-server and
ax-controller must carry a digest (values.schema.json, D075: AX publishes no images, so a tag is whatever was pushed
last); AX's own Redis is pinned by digest in values.yaml.
*/}}
{{- define "skanyxx.axImage" -}}
{{- if not (or .image.tag .image.digest) -}}
{{- fail (printf "%s needs a digest (or at least a tag)" .what) -}}
{{- end -}}
{{- $ref := .image.repository -}}
{{- with .image.tag -}}{{- $ref = printf "%s:%s" $ref . -}}{{- end -}}
{{- with .image.digest -}}{{- $ref = printf "%s@%s" $ref . -}}{{- end -}}
{{- $ref -}}
{{- end -}}

{{/* Sandboxes:Address: the in-chart ax-server with ax.install (D186), else the AX the operator runs. */}}
{{- define "skanyxx.axAddress" -}}
{{- if .Values.ax.install -}}
{{- printf "http://%s.%s.svc:8080" (include "skanyxx.component" (dict "ctx" . "name" "ax-server")) .Release.Namespace -}}
{{- else -}}
{{- required "ax.enabled=true needs ax.address (an AX you run) or ax.install=true (the chart installs AX)" .Values.ax.address -}}
{{- end -}}
{{- end -}}

{{/*
The installed AX's Redis as YAML {address, secret, key, pod}: the bundled Dragonfly (D185), else external.redis, else
AX's own Redis (ax.yaml, D189) with a generated password. `pod` names the in-chart pods to fence egress to (empty = a
BYO host).
*/}}
{{- define "skanyxx.axRedis" -}}
{{- if .Values.dragonfly.enabled -}}
address: {{ printf "dragonfly.%s.svc:6379" .Release.Namespace }}
pod: dragonfly
{{- if .Values.dragonfly.passwordFromSecret.enable }}
secret: {{ .Values.dragonfly.passwordFromSecret.existingSecret.name }}
key: {{ .Values.dragonfly.passwordFromSecret.existingSecret.key }}
{{- end }}
{{- else if .Values.external.redis.address -}}
address: {{ .Values.external.redis.address | quote }}
secret: {{ .Values.external.redis.existingSecret | quote }}
key: password
{{- else -}}
address: {{ printf "%s.%s.svc:6379" (include "skanyxx.component" (dict "ctx" . "name" "ax-redis")) .Release.Namespace }}
pod: ax-redis
secret: {{ include "skanyxx.component" (dict "ctx" . "name" "ax-redis") }}
key: password
{{- end -}}
{{- end -}}

{{/* The NetworkPolicy egress rule to the installed AX's Redis: its in-chart pods, else any host on that port. */}}
{{- define "skanyxx.axRedisEgress" -}}
{{- $redis := include "skanyxx.axRedis" . | fromYaml -}}
- ports:
    - port: {{ last (splitList ":" $redis.address) | int }}
  {{- with $redis.pod }}
  to:
    - podSelector:
        matchLabels:
          {{- include "skanyxx.selectorLabels" (dict "ctx" $ "name" .) | nindent 10 }}
  {{- end }}
{{- end -}}

{{/* D190-D194: sandbox memory is on (the schema ties it to ax.enabled). Renders "true" or nothing. */}}
{{- define "skanyxx.sandboxMemory" -}}
{{- if and .Values.ax.enabled .Values.ax.sandboxes.memory.enabled -}}true{{- end -}}
{{- end -}}

{{/* The Secret holding the sandbox token signing key (key `token-key`). */}}
{{- define "skanyxx.sandboxTokenSecret" -}}
{{- default (printf "%s-sandbox-token" (include "skanyxx.fullname" .)) .Values.ax.sandboxes.memory.existingSecret -}}
{{- end -}}
