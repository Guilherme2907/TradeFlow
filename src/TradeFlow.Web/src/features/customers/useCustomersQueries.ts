import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  addCustomerContract,
  createCustomerAccount,
  fetchAllCustomerAccounts,
  fetchMyCustomerAccount,
  type AddCustomerContractPayload,
  type CreateCustomerAccountPayload
} from './customersApi';

export const CUSTOMERS_QUERY_KEY = ['customers'] as const;

export function useMyCustomerAccountQuery(enabled = true) {
  return useQuery({
    queryKey: [...CUSTOMERS_QUERY_KEY, 'me'],
    queryFn: fetchMyCustomerAccount,
    enabled,
    staleTime: 30_000
  });
}

export function useAllCustomerAccountsQuery(enabled = false) {
  return useQuery({
    queryKey: [...CUSTOMERS_QUERY_KEY, 'all'],
    queryFn: fetchAllCustomerAccounts,
    enabled,
    staleTime: 30_000
  });
}

export function useCreateCustomerAccountMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateCustomerAccountPayload) => createCustomerAccount(payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: CUSTOMERS_QUERY_KEY });
    }
  });
}

export function useAddCustomerContractMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ customerId, payload }: { customerId: string; payload: AddCustomerContractPayload }) =>
      addCustomerContract(customerId, payload),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: CUSTOMERS_QUERY_KEY });
    }
  });
}
