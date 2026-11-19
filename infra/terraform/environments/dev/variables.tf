variable "location" {
  type        = string
  description = "Primary Azure region for the dev environment."
  default     = "eastus2"
}

variable "unique_suffix" {
  type        = string
  description = "Short alphanumeric suffix ensuring globally unique Key Vault and ACR names."
  default     = "001"
}

variable "authorized_ip_ranges" {
  type        = list(string)
  description = "Explicit CIDR ranges allowed to access the AKS Kubernetes API server and Key Vault during lab testing (override in terraform.tfvars)."
  default     = ["198.51.100.0/24"]
}

variable "secret_expiration_date" {
  type        = string
  description = "RFC3339 expiration timestamp for rotated database and Redis secrets in Key Vault."
  default     = "2027-12-31T23:59:59Z"
}
