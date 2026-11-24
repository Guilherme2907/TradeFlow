import { apiFetch } from '../../shared/api/httpClient';

export interface CustomerContractDto {
  contractId: string;
  contractNumber: string;
  title: string;
  termsText: string;
  discountPercent: number;
  effectiveFrom: string;
  expiresAt: string;
  isActive: boolean;
}

export interface CustomerAccountDto {
  customerId: string;
  companyName: string;
  taxId: string;
  tier: string;
  creditLimit: number;
  currency: string;
  paymentTermsDays: number;
  isActive: boolean;
  contracts: CustomerContractDto[];
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreateCustomerAccountPayload {
  companyName: string;
  taxId: string;
  tier: string;
  creditLimit: number;
  currency?: string;
  paymentTermsDays?: number;
}

export interface AddCustomerContractPayload {
  contractNumber: string;
  title: string;
  termsText: string;
  discountPercent: number;
  effectiveFrom: string;
  expiresAt: string;
}

export async function fetchMyCustomerAccount(): Promise<CustomerAccountDto> {
  return apiFetch<CustomerAccountDto>('/api/customers/me');
}

export async function fetchAllCustomerAccounts(): Promise<CustomerAccountDto[]> {
  return apiFetch<CustomerAccountDto[]>('/api/customers');
}

export async function createCustomerAccount(payload: CreateCustomerAccountPayload): Promise<string> {
  return apiFetch<string>('/api/customers', {
    method: 'POST',
    body: JSON.stringify(payload)
  });
}

export async function addCustomerContract(
  customerId: string,
  payload: AddCustomerContractPayload
): Promise<string> {
  return apiFetch<string>(`/api/customers/${customerId}/contracts`, {
    method: 'POST',
    body: JSON.stringify(payload)
  });
}
