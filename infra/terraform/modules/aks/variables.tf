variable "name_prefix" {
  type        = string
  description = "Naming prefix applied to AKS, ACR, and Log Analytics resources."
}

variable "acr_name" {
  type        = string
  description = "Globally unique alphanumeric Azure Container Registry name."
}

variable "acr_sku" {
  type        = string
  description = "Azure Container Registry SKU (Basic, Standard, or Premium)."
  default     = "Standard"
}

variable "acr_public_network_access_enabled" {
  type        = bool
  description = "Controls public network access to Azure Container Registry."
  default     = false
}

variable "location" {
  type        = string
  description = "Azure region where AKS and ACR are deployed."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group hosting the AKS cluster and ACR."
}

variable "aks_subnet_id" {
  type        = string
  description = "Virtual network subnet ID for AKS nodes and Azure CNI pods."
}

variable "key_vault_id" {
  type        = string
  description = "Azure Key Vault resource ID accessed by AKS pods via Workload Identity."
}

variable "kubernetes_version" {
  type        = string
  description = "Kubernetes control plane and node version."
  default     = "1.30"
}

variable "system_node_vm_size" {
  type        = string
  description = "VM SKU for the critical system addons node pool."
}

variable "system_node_min_count" {
  type        = number
  description = "Minimum node count for the system node pool."
}

variable "system_node_max_count" {
  type        = number
  description = "Maximum node count for the system node pool."
}

variable "workload_node_vm_size" {
  type        = string
  description = "VM SKU for the TradeFlow application workload node pool."
}

variable "workload_min_count" {
  type        = number
  description = "Minimum node count for the application workload node pool."
}

variable "workload_max_count" {
  type        = number
  description = "Maximum node count for the application workload node pool."
}

variable "service_cidr" {
  type        = string
  description = "CIDR block reserved for Kubernetes ClusterIP services (must not overlap VNet)."
  default     = "172.16.0.0/16"
}

variable "dns_service_ip" {
  type        = string
  description = "IP address assigned to kube-dns inside service_cidr."
  default     = "172.16.0.10"
}

variable "authorized_ip_ranges" {
  type        = list(string)
  description = "Explicit CIDR ranges authorized to connect to the AKS Kubernetes API server."
}

variable "log_retention_days" {
  type        = number
  description = "Log Analytics workspace retention in days."
  default     = 30
}

variable "tags" {
  type        = map(string)
  description = "Standard governance tags applied to all resources."
  default     = {}
}
