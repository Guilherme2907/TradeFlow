import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from './features/auth/AuthContext';
import { OrdersDashboardPage } from './features/orders/OrdersDashboardPage';
import { ApiProblemError } from './shared/api/problemDetails';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: (failureCount, error) => {
        if (error instanceof ApiProblemError && error.status >= 400 && error.status < 500) {
          return false;
        }
        return failureCount < 1;
      },
      refetchOnWindowFocus: false
    }
  }
});

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <OrdersDashboardPage />
      </AuthProvider>
    </QueryClientProvider>
  );
}

export default App;
