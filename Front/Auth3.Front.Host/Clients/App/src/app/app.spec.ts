import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { appConfig } from './app.config';

describe( 'App', () => {
  beforeEach( async () => {
    await TestBed.configureTestingModule( {
      // The DI is fully configured (CKGenAppModule.Providers).
      providers: appConfig.providers,
      imports: [App]
    } ).compileComponents();
  } );

  it( 'should create the app', () => {
    const fixture = TestBed.createComponent( App );
    expect( fixture.componentInstance ).toBeTruthy();
  } );
} );
