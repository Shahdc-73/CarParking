import { cleanParams } from './paging';

describe('cleanParams', () => {
  it('drops empty, null and undefined values but keeps 0 and false', () => {
    expect(
      cleanParams({
        search: '',
        role: null,
        sort: undefined,
        page: 1,
        isActive: false,
        offset: 0,
        q: 'x',
      }),
    ).toEqual({
      page: 1,
      isActive: false,
      offset: 0,
      q: 'x',
    });
  });
});
