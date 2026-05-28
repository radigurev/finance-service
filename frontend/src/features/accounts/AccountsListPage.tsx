import { Alert, Box, Button, Card, CardContent, Typography } from '@mui/material';
import { DataGrid, type GridColDef } from '@mui/x-data-grid';
import RefreshIcon from '@mui/icons-material/Refresh';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api } from '@/shared/api/axios';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import { useLayoutStore } from '@/shared/stores/layout';

interface AccountDto {
  id: number;
  code: string;
  name: string;
  type: 'Asset' | 'Liability' | 'Equity' | 'Revenue' | 'Expense';
  parentId: number | null;
  isActive: boolean;
  countryCode: string;
}

async function fetchAccounts(): Promise<AccountDto[]> {
  const { data } = await api.get<AccountDto[]>('/accounts');
  return data;
}

export function AccountsListPage() {
  const { t } = useTranslation();
  const density = useLayoutStore((s) => s.density);
  const isCompact = useLayoutStore((s) => s.isCompact);

  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ['accounts'],
    queryFn: fetchAccounts
  });

  const columns: GridColDef<AccountDto>[] = [
    { field: 'code', headerName: t('accounts.code'), width: 140 },
    { field: 'name', headerName: t('accounts.name'), flex: 1, minWidth: 200 },
    {
      field: 'type',
      headerName: t('accounts.type'),
      width: 160,
      valueFormatter: (value: AccountDto['type']) => t(`accounts.type_${value}`)
    },
    { field: 'countryCode', headerName: t('accounts.country'), width: 100 },
    {
      field: 'isActive',
      headerName: t('accounts.active'),
      width: 100,
      type: 'boolean'
    }
  ];

  return (
    <Card>
      <CardContent sx={{ p: isCompact ? 2 : 3 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', mb: isCompact ? 1 : 2 }}>
          <Typography variant="h5" sx={{ flexGrow: 1 }}>
            {t('accounts.title')}
          </Typography>
          <Button startIcon={<RefreshIcon />} onClick={() => refetch()} size={isCompact ? 'small' : 'medium'}>
            {t('accounts.refresh')}
          </Button>
        </Box>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {getApiErrorMessage(error, t)}
          </Alert>
        )}

        <DataGrid
          autoHeight
          density={density}
          loading={isLoading}
          rows={data ?? []}
          columns={columns}
          disableRowSelectionOnClick
          slotProps={{
            loadingOverlay: { variant: 'linear-progress', noRowsVariant: 'skeleton' }
          }}
          localeText={{
            noRowsLabel: t('accounts.empty')
          }}
        />
      </CardContent>
    </Card>
  );
}
